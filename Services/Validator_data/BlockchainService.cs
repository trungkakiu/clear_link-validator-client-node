using WorkerService1.Model;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WorkerService1.Services.Validator_data
{
    public class BlockchainService
    {
        private readonly LevelDbStorage _db;

        private const string LatestHeightKey = "block_latest_height";

        public BlockchainService(LevelDbStorage storage)
        {
            _db = storage;
        }
        public List<Block> GetBlocksByHeightRange(int from, int to)
        {
            var result = new List<Block>();

            for (int h = from; h <= to; h++)
            {
                var key = $"block_height_{h}";
                var json = _db.Get(key);

                if (json == null)
                    break;

                var block = JsonSerializer.Deserialize<Block>(json);
                if (block != null)
                    result.Add(block);
            }

            return result;
        }

        public bool DeleteBlockByHeight(int height)
        {
            try
            {
                var blockKey = $"block_height_{height}";
                var json = _db.Get(blockKey);

                if (json == null)
                    return true;

                var block = JsonSerializer.Deserialize<Block>(json);
                if (block == null)
                    return false;

                _db.Delete(blockKey);

                if (!string.IsNullOrEmpty(block.Hash))
                {
                    _db.Delete($"block_hash_{block.Hash}");
                }

                if (!string.IsNullOrEmpty(block.current_id))
                {
                    var prevHeightStr = _db.Get($"index_history_{block.current_id}_{height - 1}");
                    if (prevHeightStr != null)
                    {
                        _db.Put($"index_current_{block.current_id}", (height - 1).ToString());
                    }
                    else
                    {
                        _db.Delete($"index_current_{block.current_id}");
                    }

                    _db.Delete($"index_history_{block.current_id}_{block.Height}");
                }


                if (!string.IsNullOrEmpty(block.type))
                {
                    _db.Delete($"index_type_{block.type}_{block.current_id}_{block.status}");
                }

                _db.Put(LatestHeightKey, (height - 1).ToString());

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DeleteBlockByHeight ERROR] " + ex);
                return false;
            }
        }

        public List<BlockAnchor> GetAnchorBlocks(int limit)
        {
            var anchors = new List<BlockAnchor>();

            int latestHeight = GetLatestHeight();
            if (latestHeight < 0) return anchors;

            int count = 0;

            for (int h = latestHeight; h >= 0 && count < limit; h--)
            {
                var block = GetBlockByHeight(h);
                if (block == null || string.IsNullOrEmpty(block.Hash))
                    continue;

                anchors.Add(new BlockAnchor
                {
                    Height = block.Height,
                    Hash = block.Hash
                });

                count++;
            }

            return anchors;
        }

        public void RollbackToHeight(int forkPoint)
        {
            int latest = GetLatestHeight();
            if (latest <= forkPoint) return;

            for (int h = latest; h > forkPoint; h--)
            {
                if (!DeleteBlockByHeight(h))
                {
                    throw new Exception($"Rollback failed at height {h}");
                }
            }
        }

        public Block? GetBlockByHeight(long height)
        {
            var json = _db.Get($"block_height_{height}");
            return json == null ? null : JsonSerializer.Deserialize<Block>(json);
        }

        public Block? GetLatestBlockByCurrentId(string currentId)
        {
            var heightStr = _db.Get($"index_current_{currentId}");
            if (heightStr == null) return null;

            return GetBlockByHeight(long.Parse(heightStr));
        }

        public Block? GetBlockByType(string type, string currentId, string status)
        {
            var heightStr = _db.Get($"index_type_{type}_{currentId}_{status}");
            if (heightStr == null) return null;

            return GetBlockByHeight(long.Parse(heightStr));
        }

        public int GetLatestHeight()
        {
            var val = _db.Get(LatestHeightKey);
            if (string.IsNullOrWhiteSpace(val)) return -1;

            return int.TryParse(val, out int h) ? h : -1;
        }
        public Block? GetBlock(int height)
        {
            var json = _db.Get($"block_height_{height}");
            return json == null ? null : JsonSerializer.Deserialize<Block>(json);
        }

        public void UpdateBlockStatus(Block? oldBlock, string newStatus)
        {
            if (oldBlock == null) return;

            string oldStatus = oldBlock.status;

            oldBlock.status = newStatus;

         
            string updatedJson = JsonSerializer.Serialize(oldBlock);
            _db.Put($"block_height_{oldBlock.Height}", updatedJson);

            _db.Delete($"index_type_status_{oldBlock.type}_{oldStatus}_{oldBlock.current_id}");
            _db.Put(
                $"index_type_status_{oldBlock.type}_{newStatus}_{oldBlock.current_id}",
                oldBlock.Height.ToString()
            );

            if (oldStatus == "active" && newStatus != "active")
            {
                _db.Delete($"index_current_{oldBlock.current_id}");
            }

            if (newStatus != "active")
            {
                _db.Delete($"index_type_{oldBlock.type}_{oldBlock.current_id}");
            }
        }

        public static string SignData(string nodeId, long timestamp, string privateKeyPem)
        {
            var data = $"{nodeId}|{timestamp}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            var bytes = Encoding.UTF8.GetBytes(data);

            var signature = rsa.SignData(
                bytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            return Convert.ToBase64String(signature);
        }

        public Block? GetLatestBlock()
        {
            int height = GetLatestHeight();
            return height < 0 ? null : GetBlock(height);
        }

        public Boolean SaveBlock(Block block)
        {
            var block_current = GetBlockByHeight(block.Height);
            if (block_current != null) return false;

            string json = JsonSerializer.Serialize(block);

            _db.Put($"block_height_{block.Height}", json);

            _db.Put($"block_hash_{block.Hash}", block.Height.ToString());

            if (!string.IsNullOrEmpty(block.current_id))
            {
                _db.Put($"index_current_{block.current_id}", block.Height.ToString());
            }


            if (!string.IsNullOrEmpty(block.type) && !string.IsNullOrEmpty(block.current_id))
            {
                _db.Put($"index_type_{block.type}_{block.current_id}_{block.status}", block.Height.ToString());
            }

            if (!string.IsNullOrEmpty(block.current_id))
            {
                _db.Put($"index_history_{block.current_id}_{block.Height}", "1");
            }

            _db.Put(LatestHeightKey, block.Height.ToString());

            return true;
        }


        public string ComputeMerkleRoot(List<string> hashes)
        {
            if (hashes == null || hashes.Count == 0)
                return "0";

            var list = hashes.ToList();
            using var sha = SHA256.Create();

            while (list.Count > 1)
            {
                var newList = new List<string>();

                for (int i = 0; i < list.Count; i += 2)
                {
                    var left = list[i];
                    var right = (i + 1 < list.Count) ? list[i + 1] : left;

                    var combined = Encoding.UTF8.GetBytes(left + right);
                    var hash = sha.ComputeHash(combined);
                    var hex = BitConverter.ToString(hash).Replace("-", "").ToLower();

                    newList.Add(hex);
                }

                list = newList;
            }

            return list[0];
        }

        public string RecomputeBlockHash(Block block)
        {
            using var sha = SHA256.Create();

            string input =
                $"{block.Height}|" +
                $"{Norm(block.PreviousHash)}|" +
                $"{Norm(block.current_id)}|" +
                $"{Norm(block.Owner_id)}|" +
                $"{block.Version}|" +
                $"{Norm(block.type)}" +
                $"{Norm(block.MerkleRoot)}";

            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);

            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string Norm(string? v)
        {
            if (v == null) return "null";
            return v.Trim();
        }

        public List<Block> GetAllBlocks()
        {
            var blocks = new List<Block>();
            int height = GetLatestHeight();

            Console.WriteLine("DEBUG GetAllBlocks height=" + height);

            if (height < 1) return blocks;

            for (int i = 1; i <= height; i++)
            {
                var block = GetBlock(i);
                if (block != null)
                    blocks.Add(block);
            }

            return blocks;
        }

    }
}
