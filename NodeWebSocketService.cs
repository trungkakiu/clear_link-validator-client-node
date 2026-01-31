using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using WorkerService1.Controller;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

public class NodeWebSocketService
{
    private ClientWebSocket _ws;
    private readonly ILogger<NodeWebSocketService> _logger;
    private readonly NodeConfigDto _config;
    private readonly ValidateController _controller;
    private readonly BlockchainService _chain;
    private readonly LevelDbStorage _storage;
    private readonly NodeDatabase _nodeDb;
    private readonly NodeRuntimeState _runtime;
    public TaskCompletionSource<bool> ConnectedTcs = new();
    private readonly PerformanceCounter cpuCounter =
    new PerformanceCounter("Processor", "% Processor Time", "_Total");
    private bool _running = false;
    private bool _initSent = false;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Task? _startTask;

    public NodeWebSocketService(NodeRuntimeState runtime,ILogger<NodeWebSocketService> logger, NodeConfigDto config, ValidateController controller, LevelDbStorage storage, BlockchainService chain, NodeDatabase nodeDb)
    {

        _logger = logger;
        _runtime = runtime;
        _config = config;
        _storage = storage;
        
        _controller = controller;
        _config = config;
        _chain = chain;
        _nodeDb = nodeDb;
    }
    public string CurrentStatus => _nodeDb.GetStatus();
    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
    enum WsState
    {
        Disconnected,
        Connecting,
        InitSent,
        Authenticated
    }

    private WsState _state = WsState.Disconnected;

    public async Task Start(CancellationToken token)
    {
        await _startLock.WaitAsync(token);
        try
        {
            if (_startTask != null && !_startTask.IsCompleted)
                return;

            _startTask = StartCore(token);
        }
        finally
        {
            _startLock.Release();
        }

        await _startTask;
    }

    private async Task StartCore(CancellationToken token)
    {
        _running = true;
        var uri = new Uri("ws://192.168.110.197:5099");

        while (!token.IsCancellationRequested && _running)
        {
            try
            {
                _ws?.Abort();
                _ws?.Dispose();

                _ws = new ClientWebSocket();
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                _state = WsState.Connecting;

                await _ws.ConnectAsync(uri, token);

                await SendInit(token);
                _state = WsState.InitSent;

                await ListenLoop(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WS error. Reconnecting in 3s...");
                ResetState();
                await Task.Delay(3000, token);
            }
        }
    }

    private async Task SendInit(CancellationToken token)
    {
        var height = 0;
        var hash = "GENESIS";
        var status = _nodeDb.GetStatus();

        var latest = _chain.GetLatestBlock();
        if (latest != null)
        {
            height = latest.Height;
            hash = latest.Hash;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signature = BlockchainService.SignData(
            _config.NodeId,
            timestamp,
            _config.private_key
        );

        await Send(new
        {
            type = "init",
            nodeId = _config.NodeId,
            height,
            hash,
            node_status = status,
            node_type = "client_node",
            role = _config.Role,
            signature,
            timestamp,
            os = Environment.OSVersion.ToString()
        }, token);
    }

    private async Task SafeReconnect(CancellationToken token)
    {
        try
        {
            if (_ws != null)
            {
                _ws.Abort();
                _ws.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WS error");
            await SafeReconnect(token);
        }

        ResetState();
        await Task.Delay(3000, token);
    }

    private void ResetState()
    {
        _state = WsState.Disconnected;
        SessionContext.Instance.Clear();
    }

    private async Task ListenLoop(CancellationToken token)
    {
        var buffer = new byte[4096];

        if (_state == WsState.Authenticated)
        {
            _initSent = true;
        }

        while (!token.IsCancellationRequested)
        {
            if (_ws.State != WebSocketState.Open)
                break;

            WebSocketReceiveResult res;
            var memory = new ArraySegment<byte>(buffer);

            try
            {
                res = await _ws.ReceiveAsync(memory, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WS receive error");
                break;
            }

            if (res.MessageType == WebSocketMessageType.Close)
            {
              
                _logger.LogWarning("WS closed by server");
                break;
            }

            var msgString = Encoding.UTF8.GetString(buffer, 0, res.Count);
            _logger.LogInformation("WS Received: " + msgString);


            try
            {
                var json = JsonDocument.Parse(msgString);
                var root = json.RootElement;

               
                if (_state == WsState.Authenticated &&
                !string.IsNullOrEmpty(SessionContext.Instance.SessionId))
                {
                    await Send(new
                    {
                        type = "client_log",
                        command = $"[CLIENT] - [{_config.NodeId}] RECEIVER",
                        sessionId = SessionContext.Instance.SessionId,
                        nodeId = _config.NodeId,
                        content = root.ToString()
                    }, token);
                }


                var type = root.GetProperty("type").GetString();
                string sessionId = root.TryGetProperty("sessionId", out var sid)
                      ? sid.GetString() ?? SessionContext.Instance.SessionId
                      : SessionContext.Instance.SessionId;

                var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _nodeDb.ChangeLastActive(time);


                if (type == "command")
                {   
                  
                    string command = root.GetProperty("command").GetString();
                    _logger.LogInformation($"WS Command Received: {command}");
                    string requestId = root.GetProperty("requestId").GetString();
                    var status = _nodeDb.GetStatus();

                    if (command == "get_status")
                    {
                        if(_config.Status != "active")
                        {
                            await Send(new
                            {
                                type = "command_response",
                                command = "get_status",
                                sessionId = sessionId,
                                requestId = requestId,
                                nodeId = _config.NodeId,
                                status = "node " + status,
                                time = DateTime.UtcNow
                            }, token);
                            return;
                        }
                        var Nodestatus = GetNodeStatus();

                        await Send(new
                        {
                            type = "command_response",
                            command = "get_status",
                            sessionId = sessionId,
                            requestId = requestId,
                            nodeId = _config.NodeId,
                            status = Nodestatus,
                            time = DateTime.UtcNow
                        }, token);

                        _logger.LogInformation("WS: Sent status response!");
                    }

                    if (command == "get_vote")
                    {

                        string voteRoundId = root.GetProperty("voteRoundId").GetString();
                        _logger.LogInformation("WS COMMAND: get_vote RECEIVED!");

                        var payloadElement = root.GetProperty("payload");

                        var votedata = payloadElement.Deserialize<VotePayloadDto>();
                        
                        _logger.LogInformation("VotePayload received: " + JsonSerializer.Serialize(votedata));

                        var voteResult = new VoteResultDto();


                        if (status != "active")
                        {
                            await Send(new
                            {
                                type = "vote_response",
                                command = "vote_result",
                                sessionId = sessionId,
                                requestId = requestId,
                                nodeId = _config.NodeId,
                                voteRoundId = voteRoundId,
                                payload = votedata.client_hash,
                                signature = voteResult.signature,
                                ok = false,
                                node_type = "client",
                                error = "node " + status,
                                time = DateTime.UtcNow
                            }, token);
                            return;
                        }

                        if (votedata.command_type == "new")
                        {
                            voteResult = _controller.GetFirstVote(votedata);
                        }
                        else
                        {
                            voteResult = _controller.GetVote(votedata);
                        }
                        await Send(new
                        {
                            type = "vote_response",
                            command = "vote_result",
                            requestId = requestId,
                            sessionId = sessionId,
                            nodeId = _config.NodeId,
                            voteRoundId = voteRoundId,
                            payload = votedata.client_hash,
                            signature = voteResult.signature,
                            ok = voteResult.ok,
                            node_type = "client",
                            error = voteResult.error,
                            time = DateTime.UtcNow
                        }, token);

                        _logger.LogInformation("Vote response sent to server!");
                    }

                    if (command == "drop_precheck_vote")
                    {
                        var payloadElement = root.GetProperty("payload");
                        var dto = payloadElement.Deserialize<VoteDropProductDto>();

                        if (dto == null || dto.products == null || dto.products.Count == 0)
                        {
                            return;
                        }

                        var voteResult = _controller.GetDropVote(dto);

                        if (voteResult.RC != 200)
                        {
                            Console.WriteLine("[DROP VOTE] Vote failed: " + voteResult.RM);
                            return;
                        }


                        var options = new JsonSerializerOptions
                        {
                          
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

                            WriteIndented = false,

                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var votePayload = voteResult.RD;
                        string payloadJson = JsonSerializer.Serialize(votePayload, options);

                        var signer = RSA.Create();
                        signer.ImportFromPem(_config.private_key);

                        var signatureBytes = signer.SignData(
                            Encoding.UTF8.GetBytes(payloadJson),
                            HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1
                        );

                        var votepayload = new
                        {
                            votes = voteResult.RD
                        };
                        var signature = Convert.ToBase64String(signatureBytes);

                        await Send(new
                        {
                            type = "drop_precheck_vote_ack",
                            voteRoundId = root.GetProperty("voteRoundId").GetString(),
                            sessionId = SessionContext.Instance.SessionId,
                            nodeId = _config.NodeId,
                            payloadJson = payloadJson,
                            votePayload = votepayload,
                            signature = signature,
                            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        }, token);


                    }

                    if (command == "pair_user")
                    {
                        _logger.LogInformation("PAIR_USER received!");

                        var payloadElement = root.GetProperty("payload");
                        var dto = payloadElement.Deserialize<PairUserPayloadDto>();

                        if (status != "active")
                        {
                            await Send(new
                            {
                                type = "pair_user_response",
                                requestId = requestId,
                                ok = false,
                                block = "node " + status,
                                time = DateTime.UtcNow
                            }, token);
                            return;
                        }

                        var result = _controller.PairUser(dto);

                        await Send(new
                        {
                            type = "pair_user_response",
                            requestId = requestId,
                            ok = result.RC == 200,
                            block = result.RD,
                            time = DateTime.UtcNow
                        }, token);
                    }

                    if(command == "pair_product")
                    {
                        _logger.LogInformation("PAIR_USER received!");

                        var payloadElement = root.GetProperty("payload");
                        var dto = payloadElement.Deserialize<PairProductPayloadDto>();


                        if (status != "active")
                        {
                            await Send(new
                            {
                                type = "pair_product_response",
                                requestId = requestId,
                                ok = false,
                                block = "node " + status,
                                time = DateTime.UtcNow
                            }, token);
                            return;
                        }


                        var result = _controller.PairProduct(dto);

                        await Send(new
                        {
                            type = "pair_product_response",
                            requestId = requestId,
                            ok = result.RC == 200,
                            block = result.RD,
                            time = DateTime.UtcNow
                        }, token);
                    }
                        
                    if (command == "override_block")
                    {
                        _logger.LogInformation("override_block received!");

                        var payloadElement = root.GetProperty("payload");
                        var dto = payloadElement.Deserialize<RepairBlockPayloadDto>();

                        if (status != "active")
                        {
                            await Send(new
                            {
                                type = "override_block_respone",
                                requestId = requestId,
                                ok = false,
                                block = "node " + status,
                                time = DateTime.UtcNow
                            }, token);
                            return;
                        }


                        ApiResponse result = new ApiResponse();

                        switch (dto.payload.type)
                        {
                            case "product_create":
                                result = _controller.RePairProduct(dto);
                                break;

                            default:
                                result = new ApiResponse
                                {
                                    RC = 500,
                                    RM = "Truncate options"
                                };
                                break;
                        }


                        await Send(new
                        {
                            type = "override_block_respone",
                            requestId = requestId,
                            ok = result.RC == 200,
                            block = result.RD,
                            time = DateTime.UtcNow
                        }, token);
                    }

                  
                }

                if(type == "Maintenance")
                {
                    _nodeDb.ChangeStatus("maintenance");
                    
                    await Send(new
                    {
                        type = "Maintenance_responese",
                        requestId = root.GetProperty("requestId").GetString(),
                        sessionId = sessionId,
                        ok = true,
                        nodeId = _config.NodeId,
                        message = "Node entering maintenance mode"
                    }, token);

                }

                if (type == "connected")
                {

                    var status = root.GetProperty("status").GetString();
                    SessionContext.Instance.SetSession(sessionId, _config.NodeId);
                    _nodeDb.ChangeStatus(status);
                    _state = WsState.Authenticated;

                    _logger.LogInformation($"Authenticated with session {sessionId}");

                    ConnectedTcs.TrySetResult(true);
                    await Send(new
                    {
                        type = "client_log",
                        command = $"[CLIENT] - [{_config.NodeId}] CONNECTED",
                        sessionId = sessionId,
                        nodeId = _config.NodeId,
                        content = "Node connected and authenticated"
                    }, token);



                   
                }

                if (type == "sync_response")
                {
                    try
                    {
                        var ok = root.GetProperty("ok").GetBoolean();

                        string? status = root.TryGetProperty("status", out var st)
                            ? st.GetString()
                            : null;

                        string? syncStatus = root.TryGetProperty("sync_status", out var ss)
                            ? ss.GetString()
                            : null;

                        SessionContext.Instance.SetSession(
                            sessionId,
                            _config.NodeId
                        );

                        if (!ok)
                        {
                            await Send(new
                            {
                                type = "client_log",
                                level = "WARN",
                                sessionId,
                                message = "sync_response not ok",
                                syncStatus
                            }, token);

                            if (syncStatus != "node_outlaw")
                            {
                                _runtime.SyncRetryCount++;
                                _runtime.SyncRequestInFlight = true;
                            }
                            return;
                        }

                        if (status == "fork")
                        {
                            await Send(new
                            {
                                type = "client_log",
                                sessionId,
                                level = "ERROR",
                                message = "fork detected from server"
                            }, token);

                            _nodeDb.ChangeStatus("fork");
                            return;
                        }

                        List<Block> blocks = new();

                        if (root.TryGetProperty("blocks", out var blocksProp) &&
                            blocksProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var blockEl in blocksProp.EnumerateArray())
                            {
                                var b = new Block
                                {
                                    Height = blockEl.GetProperty("Height").GetInt32(),
                                    Hash = blockEl.GetProperty("Hash").GetString()!,
                                    PreviousHash = blockEl.GetProperty("PreviousHash").GetString()!,
                                    current_id = blockEl.GetProperty("current_id").GetString()!,
                                    Owner_id = blockEl.GetProperty("Owner_id").GetString()!,
                                    status = blockEl.GetProperty("status").GetString()!,
                                    Timestamp = blockEl.GetProperty("Timestamp").GetString()!,
                                    type = blockEl.GetProperty("type").GetString()!,
                                    ValidatorSignature = blockEl.GetProperty("ValidatorSignature").GetString()!,
                                    Version = blockEl.GetProperty("Version").GetString()!,
                                    MerkleRoot = blockEl.GetProperty("MerkleRoot").GetString()!,
                                    Creator = blockEl.TryGetProperty("Creator", out var cr)
                                        ? cr.GetString()
                                        : null

                                };

                                if (blockEl.TryGetProperty("headerRaw", out var hr) &&
                                    hr.ValueKind == JsonValueKind.Object &&
                                    hr.TryGetProperty("data", out var dataProp) &&
                                    dataProp.ValueKind == JsonValueKind.Array)
                                {
                                    var bytes = new byte[dataProp.GetArrayLength()];
                                    int i = 0;
                                    foreach (var n in dataProp.EnumerateArray())
                                    {
                                        bytes[i++] = (byte)n.GetInt32();
                                    }
                                    b.headerRaw = bytes;
                                }
                                else
                                {
                                    throw new Exception("headerRaw missing or invalid format");
                                }

                                blocks.Add(b);
                            }
                        }

                        if (blocks.Count == 0)
                        {
                            await Send(new
                            {
                                type = "client_log",
                                sessionId,
                                level = "WARN",
                                message = "no blocks received"
                            }, token);

                            _runtime.SyncRetryCount++;
                            return;
                        }

                        blocks.Sort((a, b) => a.Height.CompareTo(b.Height));

                        var latest = _chain.GetLatestBlock();
                        var expectedHeight = (latest?.Height ?? 0) + 1;
                        var expectedPrevHash = latest?.Hash ?? "GENESIS";

                        foreach (var b in blocks)
                        {
                            if (b.Height != expectedHeight ||
                                b.PreviousHash != expectedPrevHash)
                            {
                                await Send(new
                                {
                                    type = "client_log",
                                    sessionId,
                                    level = "ERROR",
                                    message = "block validation failed",
                                    expectedHeight,
                                    actualHeight = b.Height,
                                    expectedPrevHash,
                                    actualPrevHash = b.PreviousHash
                                }, token);

                                _nodeDb.ChangeStatus("fork");
                                return;
                            }

                            var save = _chain.SaveBlock(b);
                            if (!save)
                            {
                                await Send(new
                                {
                                    type = "client_log",
                                    level = "ERROR",
                                    sessionId,
                                    message = "block save failed",
                                    expectedHeight,
                                    actualHeight = b.Height,
                                    expectedPrevHash,
                                    actualPrevHash = b.PreviousHash
                                }, token);
                                return;
                            }

                            expectedPrevHash = b.Hash;
                            expectedHeight++;
                        }

                        _nodeDb.ChangeStatus(
                            syncStatus == "complate" ? "active" : "syncing"
                        );

                        _runtime.SyncRequestInFlight = false;

                        await Send(new
                        {
                            type = "client_log",
                            sessionId,
                            level = "INFO",
                            message = "sync_response handled successfully",
                            finalHeight = expectedHeight - 1
                        }, token);
                    }
                    catch (Exception ex)
                    {
                        await Send(new
                        {
                            type = "client_log",
                            sessionId = SessionContext.Instance.SessionId,
                            level = "FATAL",
                            message = "exception while handling sync_response",
                            error = ex.Message,
                            stack = ex.StackTrace
                        }, token);

                        _runtime.SyncRetryCount++;
                        _runtime.SyncRequestInFlight = false;
                        return;
                    }
                }


                if (type == "fork_response")
                {
                    try
                    {
                        var ok = root.GetProperty("ok").GetBoolean();

                        if (!ok)
                        {
                            await Send(new
                            {
                                type = "log",
                                level = "ERROR",
                                message = "server critical"
                            }, token);
                            return;
                        }

                        var status = _nodeDb.GetStatus();
                        if(status != "fork")
                        {
                            await Send(new
                            {
                                type = "log",
                                level = "WARN",
                                message = "node status not fork"
                            }, token);
                            return;
                        }

                        var fork_point = root.GetProperty("fork_point").GetInt32();
                        var truth_pos = root.GetProperty("truth_point").GetBoolean();
                        if (fork_point != -1 && fork_point > 0)
                        {
                            var latest_block = _chain.GetLatestBlock();
                            if (latest_block == null) return;
                            for(int i = latest_block.Height; i > fork_point; i--)
                            {
                               var delete = _chain.DeleteBlockByHeight(i);

                                if (!delete)
                                {
                                    await Send(new
                                    {
                                        type = "log",
                                        level = "ERROR",
                                        message = $"delete block {i} failed"
                                    }, token);
                                    return;
                                }
                            }

                            if (truth_pos)
                            {
                                _nodeDb.ChangeStatus("syncing");

                            }
                            else
                            {
                                _nodeDb.ChangeStatus("fork");

                            }
                            await Send(new
                            {
                                type = "log",
                                level = "SUCCESS",
                                message = "one step complate"
                            }, token);
                            return;

                        }
                        else
                        {
                            await Send(new
                            {
                                type = "log",
                                level = "ERROR",
                                message = "invalid fork point"
                            }, token);
                            return;
                        }

                    }catch(Exception ex)
                    {
                        await Send(new
                        {
                            type = "log",
                            level = "FATAL",
                            nodeId = _config.NodeId,
                            massage = ex.Message,
                            stack = ex.StackTrace
                        }, token);
                        return;
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WS command parse error");
            }
        }
    }

    public async Task Send(object data, CancellationToken token)
    {
        if (_ws.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            token
        );
    }


    private (double used, double total) GetRamUsage()
    {
        var status = new MemoryStatusEx();
        if (MemoryHelper.GlobalMemoryStatusEx(status))
        {
            double total = status.TotalPhys / (1024.0 * 1024 * 1024);
            double available = status.AvailPhys / (1024.0 * 1024 * 1024);
            double used = total - available;

            return (Math.Round(used, 2), Math.Round(total, 2));
        }

        return (0, 0);
    }
    private float GetCpuUsage()
    {
        return cpuCounter.NextValue();
    }
    private (double free, double total) GetDiskUsage()
    {
        string root = Path.GetPathRoot(AppContext.BaseDirectory);
        var drive = new DriveInfo(root);

        double total = drive.TotalSize / (1024.0 * 1024 * 1024);
        double free = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);

        return (Math.Round(free, 2), Math.Round(total, 2));
    }
    private long PingMeta(string host)
    { 
        try
        {
            using Ping ping = new Ping();
            var reply = ping.Send(host, 500);

            return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }

 
    private LevelDbHealth GetDBlife()
    {
        return _storage.GetDBlife();
    }
    private object GetNodeStatus()
    {
        float cpuUsage = GetCpuUsage();
        LevelDbHealth db_life = GetDBlife();
        (double usedRam, double totalRam) = GetRamUsage();
        bool db_alive = db_life.Alive;
        bool db_canRead = db_life.CanRead;
        bool db_CanWrite = db_life.CanWrite;
        double db_FileSizeMB = db_life.FileSizeMB;
        string db_Message = db_life.Message;
        
        (double freeDisk, double totalDisk) = GetDiskUsage();
        long ping = PingMeta("192.168.1.7");

        var height = _chain.GetLatestBlock()?.Height ?? 0;

        return new
        {
            running = true,
            cpu = cpuUsage,
            height = height,
            ram_used = usedRam,
            db_alive = db_alive,
            db_canRead = db_canRead,
            db_CanWrite = db_CanWrite,
            db_FileSizeMB = db_FileSizeMB,
            db_Message = db_Message,
            ram_total = totalRam,
            disk_free = freeDisk,
            disk_total = totalDisk,
            ping = ping
        };
    }

    private VoteResultDto HandlePairUser(PairUserPayloadDto dto)
    {
        try
        {
            string payloadJson = JsonSerializer.Serialize(dto);

            using var rsaNode = RSA.Create();
            rsaNode.ImportFromPem(_config.private_key);

            byte[] sig = rsaNode.SignData(
                Encoding.UTF8.GetBytes(payloadJson),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            return new VoteResultDto
            {
                ok = true,
                signature = Convert.ToBase64String(sig),
                payload = dto
            };
        }
        catch (Exception ex)
        {
            return new VoteResultDto { ok = false, error = ex.Message };
        }
    }

       
  
}
