using LevelDB;
using System;
using System.Text;
using WorkerService1.Model;

namespace WorkerService1.Services.Validator_data
{
    public class LevelDbStorage : IDisposable
    {
        private readonly DB _db;
        private readonly NodeConfigDto _config;

        private const int NODE_ID = 1;

        public string BlockKey(string type, int height)
            => $"{type}_block_height_{height}";

        public string BlockHashKey(string type, string hash)
            => $"{type}_block_hash_{hash}";

        public string LatestKey(string type)
            => $"{type}_block_latest";

        public LevelDbStorage(NodeConfigDto config)
        {
            _config = config;

            var options = new Options
            {
                CreateIfMissing = true
            };

            _db = new DB(options, "leveldb_data");
        }

        public void Put(string key, string value)
        {
            _db.Put(Encode(key), Encode(value));
        }

        public string? Get(string key)
        {
            var data = _db.Get(Encode(key));
            return data == null ? null : Decode(data);
        }

        public void Delete(string key)
        {
            _db.Delete(Encode(key));
        }

        public bool Exists(string key)
        {
            return Get(key) != null;
        }

      
        private string NodeInfoKey => "node_info";

        public void SaveNodeInfo(NodeInfo info)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(info);
            Put(NodeInfoKey, json);
        }

        public NodeInfo? LoadNodeInfo()
        {
            var json = Get(NodeInfoKey);
            if (json == null) return null;

            return System.Text.Json.JsonSerializer.Deserialize<NodeInfo>(json);
        }

        public void ClearNodeInfo()
        {
            Delete(NodeInfoKey);
        }

        public LevelDbHealth GetDBlife()
        {
            var health = new LevelDbHealth();

            try
            {
                health.Alive = _db != null;
                if (!health.Alive)
                {
                    health.Message = "DB instance is null";
                    return health;
                }

                try
                {
                    var test = _db.Get(Encoding.UTF8.GetBytes("health_test_key"));
                    health.CanRead = true;
                }
                catch
                {
                    health.CanRead = false;
                }

                try
                {
                    var key = Encoding.UTF8.GetBytes("health_write_test");
                    var value = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString());

                    _db.Put(key, value);

                    var readBack = _db.Get(key);
                    health.CanWrite = readBack != null;
                }
                catch
                {
                    health.CanWrite = false;
                }

    
                try
                {
                    var dir = new DirectoryInfo("leveldb_data");
                    long totalSize = dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    health.FileSizeMB = Math.Round((double)totalSize / (1024 * 1024), 2);
                }
                catch
                {
                    health.FileSizeMB = -1;
                }

                health.Message = health.Alive && health.CanRead && health.CanWrite
                    ? "OK"
                    : "LevelDB may have an issue";

                return health;
            }
            catch (Exception ex)
            {
                return new LevelDbHealth
                {
                    Alive = false,
                    CanRead = false,
                    CanWrite = false,
                    FileSizeMB = -1,
                    Message = ex.Message
                };
            }
        }

        private static byte[] Encode(string s) => Encoding.UTF8.GetBytes(s);
        private static string Decode(byte[] data) => Encoding.UTF8.GetString(data);

        public void Dispose()
        {
            _db?.Dispose();
        }


    }
}
