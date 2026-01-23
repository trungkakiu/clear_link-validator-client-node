using LiteDB;
using WorkerService1.Model;

namespace WorkerService1.Services.Validator_data
{
    public class PeerService : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<PeerModel> _peers;
        private readonly NodeConfigDto _config;

        public void Dispose()
        {
            _db?.Dispose();
        }
        public PeerService(NodeConfigDto config)
        {
            _config = config;

            var dbPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                $"{_config.NodeId}_peers.db"
            );

            _db = new LiteDatabase(dbPath);

            _peers = _db.GetCollection<PeerModel>("peers");

            _peers.EnsureIndex(x => x.Url, unique: true);
        }

        public void AddPeer(string peerUrl)
        {
            if (string.IsNullOrWhiteSpace(peerUrl)) return;

            if (!_peers.Exists(x => x.Url == peerUrl))
            {
                _peers.Insert(new PeerModel
                {
                    Id = ObjectId.NewObjectId(),
                    Url = peerUrl
                });
            }
        }

        public List<string> GetPeerList()
        {
            return _peers.Query()
                         .ToList()
                         .Select(x => x.Url)
                         .ToList();
        }

        public void RemovePeer(string peerUrl)
        {
            if (string.IsNullOrWhiteSpace(peerUrl)) return;

            _peers.DeleteMany(x => x.Url == peerUrl);
        }
    }

    public class PeerModel
    {
        public ObjectId Id { get; set; }
        public string Url { get; set; } = "";
    }
}
