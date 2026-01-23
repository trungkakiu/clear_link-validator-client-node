using System.Text.Json;
using WorkerService1; 
using WorkerService1.Controller;
using WorkerService1.Model;
using WorkerService1.Services.Middle_ware;
using WorkerService1.Services.Validator_data;

var nativePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");
Environment.SetEnvironmentVariable("ROCKSDB_LIB_DIR", nativePath);

var builder = WebApplication.CreateBuilder(args);

var exeDir = AppContext.BaseDirectory;
var configDir = Path.Combine(exeDir, "configs");
Directory.CreateDirectory(configDir);


var configPath = Path.Combine(configDir, "validator_config.json");

if (!File.Exists(configPath))
{
    Console.WriteLine("validator_config.json NOT FOUND!");
    Console.WriteLine("Service will wait until file exists...");
    while (!File.Exists(configPath))
    {
        Thread.Sleep(1000);
    }
}


var json = File.ReadAllText(configPath);
var nodeConfig = JsonSerializer.Deserialize<NodeConfigDto>(json);

if (nodeConfig == null)
{
    Console.WriteLine("validator_config.json is invalid. Cannot start node.");
    return;
}

builder.Services.AddSingleton(nodeConfig);

builder.WebHost.UseUrls("http://localhost:5101");

builder.Services.AddSingleton<NodeWebSocketService>();

builder.Services.AddSingleton<NodeDatabase>();
builder.Services.AddSingleton<LevelDbStorage>(sp =>
{
    var cfg = sp.GetRequiredService<NodeConfigDto>();
    return new LevelDbStorage(cfg);
});
builder.Services.AddSingleton<BlockchainService>();
builder.Services.AddSingleton<PeerService>();
builder.Services.AddSingleton<ConsensusService>();
builder.Services.AddSingleton<NodeRuntimeState>();


builder.Services.AddSingleton<ValidatorMiddleware>();
builder.Services.AddSingleton<ValidateController>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);


builder.Services.AddHostedService<Worker>();
builder.Host.UseWindowsService();

var app = builder.Build();
var api = app.MapGroup("/api");


var controller = app.Services.GetRequiredService<ValidateController>();
var validator = app.Services.GetRequiredService<ValidatorMiddleware>();

api.MapGet("/debug/node_info", (NodeDatabase db) =>
{
    return db.GetFullNodeInfo();
});

api.MapGet("/debug/block", (NodeDatabase db) =>
{
    return db.GetFullBlock();
});

api.MapPost("/debug/forkblock", (NodeDatabase db) =>
{
    return db.CreateForkBlockAtTip("hahahaahahahah");
});


app.Run();
