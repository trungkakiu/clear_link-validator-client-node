using Azure.Core;
using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using System.Xml.Linq;
using WorkerService1.Controller;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NodeConfigDto _config;
    private readonly NodeDatabase _node;
    private readonly BlockchainService _chain;
    private readonly NodeRuntimeState _runtime;
    private readonly NodeWebSocketService _ws;

    public Worker(NodeWebSocketService ws,ILogger<Worker> logger, IServiceScopeFactory scopeFactory, NodeConfigDto config, BlockchainService chain, NodeDatabase node, NodeRuntimeState runtime)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _config = config;
        _chain = chain;
        _node = node;
        _runtime = runtime;
        _ws = ws;
    }
       
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ws = _ws;

        _ = ws.Start(stoppingToken);
        await ws.ConnectedTcs.Task;

        while (!stoppingToken.IsCancellationRequested)
        {
            var status = ws.CurrentStatus;
            await ws.Send(new
            {
                type = "client_log",
                nodeId = _config.NodeId,
                status = status,
                sessionId = SessionContext.Instance.SessionId,
                time = DateTime.UtcNow
            }, stoppingToken);

            if(status == "maintenance")
            {
                await MaintanenceMode(stoppingToken, status, ws);
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            if (status == "fork")
            {
                await ForkMode(stoppingToken, ws);
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            if (status == "syncing")
            {
                await SyncMode(stoppingToken, status, ws);
            }
            else if (status == "active")
            {
                await ActiveMode(stoppingToken, status, ws);
            }

            await Task.Delay(15000, stoppingToken);
        }


        while (!stoppingToken.IsCancellationRequested)
        {
            using (var db_scope = _scopeFactory.CreateScope())
            {
                var db = db_scope.ServiceProvider.GetRequiredService<NodeDatabase>();
            }

            await Task.Delay(3000, stoppingToken);
        }

    }

    private async Task HandleFork(
    NodeWebSocketService ws,
    string reason,
    int atHeight,
    int? gotHeight)
    {
        await ws.Send(new
        {
            type = "fork_maintenance_response",
            ok = false,
            sessionId = SessionContext.Instance.SessionId,
            reason,
            atHeight,
            gotHeight
        }, CancellationToken.None);

        _node.ChangeStatus("fork");

    
    }

    private async Task MaintanenceMode(
    CancellationToken stoppingToken,
    string status,
    NodeWebSocketService ws)
    {
        try
        {
            if (_node.GetStatus() != "maintenance")
            {
                _node.ChangeStatus("syncing");
                return;
            }

            var latest = _chain.GetLatestBlock();
            if (latest == null)
            {
                _node.ChangeStatus("syncing");
                return;
            }
   

            const int BATCH = 1000;
            int expectedHeight = 1;
            string previousHash = "GENESIS";

            while (!stoppingToken.IsCancellationRequested)
            {
                int from = expectedHeight;
                int to = expectedHeight + BATCH - 1;

                var blocks = _chain.GetBlocksByHeightRange(from, to);
                if (blocks == null || blocks.Count == 0)
                    break;

                blocks.Sort((a, b) => a.Height.CompareTo(b.Height));

                foreach (var b in blocks)
                {
                    if (b.Height != expectedHeight)
                    {
                        await HandleFork(ws, "HEIGHT_GAP", expectedHeight, b.Height);
                        return;
                    }

                    if (b.PreviousHash != previousHash)
                    {
                        await HandleFork(ws, "PREV_HASH_MISMATCH", b.Height, null);
                        return;
                    }

                    var expectedHash = _chain.ComputeBlockHashFromHeader(b.headerRaw);
                    if (b.Hash != expectedHash)
                    {
                        await HandleFork(ws, "HASH_MISMATCH", b.Height, null);
                        return;
                    }

                    previousHash = b.Hash;
                    expectedHeight++;
                }
            }

            _node.ChangeStatus("active");

            await ws.Send(new
            {
                type = "client_log",
                sessionId = SessionContext.Instance.SessionId,
                nodeId = _config.NodeId,
                message = "Maintenance complete with no fork"
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            await ws.Send(new
            {
                type = "client_log",
                sessionId = SessionContext.Instance.SessionId,
                nodeId = _config.NodeId,
                status = status,
                message = ex.Message
            }, stoppingToken);
        }
    }

    private async Task ActiveMode(CancellationToken stoppingToken, string status, NodeWebSocketService ws)
    {
        try
        {
            var latestBlock = _chain.GetLatestBlock();
            var height = 0;

            var hash = "GENESIS";
            if (latestBlock != null)
            {
                height = latestBlock.Height;
                hash = latestBlock.Hash;
            }

            await ws.Send(new
            {
                type = "heartbeat",
                nodeId = _config.NodeId,
                address = _config.Address,
                status = status,
                sessionId = SessionContext.Instance.SessionId,
                height = height,
                hash = hash,
                port = _config.Port,
                time = DateTime.UtcNow
            }, stoppingToken);

        }
        catch (Exception ex) {
            await ws.Send(new
            {
                type = "log",
                sessionId = SessionContext.Instance.SessionId,
                nodeId = _config.NodeId,
                status = status,
                massage = ex.Message
            }, stoppingToken);
        }
        
   
    }

    private async Task SyncMode(CancellationToken token, string status, NodeWebSocketService ws)
    {
        try
        {
            var now = DateTime.UtcNow;

            if (_runtime.SyncCooldownUntil != null &&
             now < _runtime.SyncCooldownUntil.Value)
            {
                _logger.LogWarning(
                    $"[SYNC] cooldown until {_runtime.SyncCooldownUntil}"
                );
                return;
            }

            if ((now - _runtime.LastSyncAttemptAt).TotalSeconds < 10)
                return;

            if (_runtime.SyncRequestInFlight)
            {

                if ((DateTime.UtcNow - _runtime.LastSyncRequestAt).TotalSeconds < 10)
                    return;

                _logger.LogWarning("[SYNC] timeout, reset inFlight");
                _runtime.SyncRequestInFlight = false;
            }


            if (_runtime.SyncRetryCount >= 500)
            {
                _runtime.SyncCooldownUntil = now.AddMinutes(30);
                _runtime.SyncRetryCount = 0;

                _logger.LogError("[SYNC] enter cooldown 30 minutes");
                return;
            }

            var latest = _chain.GetLatestBlock();
            var fromHeight = (latest?.Height ?? 0);

            _runtime.SyncRequestInFlight = true;

            _logger.LogWarning($"[SYNC] request fromHeight={fromHeight}");

            await ws.Send(new
            {
                type = "sync_request",
                sessionId = SessionContext.Instance.SessionId,
                nodeId = _config.NodeId,
                from_height = fromHeight,
                limit = 20
            }, token);
        }
        catch(Exception ex)
        {
            await ws.Send(new
            {
                type = "client_log",
                sessionId = SessionContext.Instance.SessionId,
                nodeId = _config.NodeId,
                status = status,
                massage = ex.Message
            }, token);
        }
        
    }

    private async Task ForkMode(CancellationToken token, NodeWebSocketService ws)
    {
        try
        {
            var archor_blocks = _chain.GetAnchorBlocks(50);
            var latest_block = _chain.GetLatestBlock();

            await ws.Send(new
            {
                type = "archor_block_fork",
                nodeId = _config.NodeId,
                sessionId = SessionContext.Instance.SessionId,
                archor_block = archor_blocks,
                height = latest_block?.Height ?? 0,
                status = "fork",
                timestamp = DateTime.UtcNow
            }, token);
        }
        catch (Exception ex)
        {
            await ws.Send(new
            {
                type = "client_log",
                sessionId = SessionContext.Instance.SessionId,
                level = "ERROR",
                nodeId = _config.NodeId,
                message = ex.Message
            }, token);
        }
    }

}
