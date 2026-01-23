using LiteDB;
using System;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;

namespace WorkerService1.Controller
{
    public class NodeDatabase
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<NodeInfo> _nodes;
        private readonly BlockchainService _chain;
        private bool _isActive;

        public NodeDatabase(BlockchainService chain)
        {

            _chain = chain;

            var dbPath = Path.Combine(
                AppContext.BaseDirectory,
                "validator_state.db"
            );

            _db = new LiteDatabase(dbPath);
            _nodes = _db.GetCollection<NodeInfo>("NodeInfo");

            var node = _nodes.FindOne(x => x.Id == 1);
            if (node == null)
            {
                node = new NodeInfo
                {
                    Id = 1,
                    NodeId = "validator_1",
                    Status = "active",
                    LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                _nodes.Insert(node);
            }
        }

        public Block CreateForkBlockAtTip(string fakeCreator)
        {
            var latest = _chain.GetLatestBlock();
            if (latest == null) throw new Exception("No latest block");

            var forkBlock = new Block
            {
                Height = latest.Height + 1,
                PreviousHash = latest.PreviousHash,
                Creator = fakeCreator,
                current_id = "fork_test_" + Guid.NewGuid(),
                
                Owner_id = latest.Owner_id,
                type = "fork_test",
                status = "active",
                Timestamp = latest.Timestamp,
                MerkleRoot = Guid.NewGuid().ToString("N"),
                Version = latest.Version
            };

            forkBlock.Hash = latest.Hash;

            _chain.SaveBlock(forkBlock);

            return forkBlock;
        }

        public string GetStatus()
        {
            var node = _nodes.FindOne(x => x.Id == 1);

            if (node == null)
            {
                return ("unknown");
            }

            return (node.Status);
        }


        public IResult ChangeStatus(string status)
        {
            if (status == null)
                return Results.Json(new { RM = "Thiếu dữ liệu payload", RC = -401 });


            var node = _nodes.FindOne(x => x.Id == 1);
            if (node == null)
            {
                return Results.Json(new
                {
                    RM = "Không tìm thấy node local",
                    RC = -404
                });
            }

            node.Status = status;
            node.LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _nodes.Upsert(node); 

            _isActive = status == "active";

            return Results.Json(new
            {
                RC = 200,
                RM = "Cập nhật trạng thái thành công",
                RD = node
            });
        }

        public void UpdateLastActive()
        {
            var node = _nodes.FindOne(x => x.Id == 1);
            if (node == null) return;

            node.LastActive = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _nodes.Upsert(node); // UPDATED
        }

        public IResult GetFullNodeInfo()
        {
            var list = _nodes.FindAll().ToList();
            return Results.Json(new
            {
                RC = 200,
                RM = "Dump database",
                RD = list
            });
        }

        public IResult GetFullBlock()
        {
   
            var blocks = _chain.GetAllBlocks();

            return Results.Json(new
            {
                RC = 200,
                RM = "Dump all block chains",
                RD = blocks
            });
        }
    }
}
