using LiteDB;
using System;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;
using System.Security.Cryptography;
using System.Text;

namespace WorkerService1.Controller
{
    public class NodeDatabase
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<NodeInfo> _nodes;
        private readonly NodeConfigDto _config;
        private readonly BlockchainService _chain;
        private bool _isActive;

        public NodeDatabase(BlockchainService chain, NodeConfigDto config)
        {

            _chain = chain;
            _config = config;

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


        public IResult TestKey(string value)
        {
            var privateKeyPem = _config.private_key;
            var publicKeyPem = _config.public_key;

            // Kiểm tra xem config đã nạp được key chưa
            if (string.IsNullOrEmpty(privateKeyPem) || string.IsNullOrEmpty(publicKeyPem))
            {
                return Results.Json(new
                {
                    RM = "Lỗi: Không tìm thấy Private Key hoặc Public Key trong cấu hình.",
                    RC = -404
                });
            }

            string dataToTest = string.IsNullOrEmpty(value) ? "clearlink_test_payload" : value;

            try
            {

                using var rsaPrivate = RSA.Create();
                rsaPrivate.ImportFromPem(privateKeyPem);
                
                byte[] dataBytes = Encoding.UTF8.GetBytes(dataToTest);
                byte[] signatureBytes = rsaPrivate.SignData(
                    dataBytes, 
                    HashAlgorithmName.SHA256, 
                    RSASignaturePadding.Pkcs1
                );
                string signatureBase64 = Convert.ToBase64String(signatureBytes);

                using var rsaPublic = RSA.Create();
                rsaPublic.ImportFromPem(publicKeyPem);
                
                bool isValid = rsaPublic.VerifyData(
                    dataBytes, 
                    signatureBytes, 
                    HashAlgorithmName.SHA256, 
                    RSASignaturePadding.Pkcs1
                );

                if (isValid)
                {
                    return Results.Json(new
                    {
                        RM = "Thành công! Cặp Key hoàn toàn khớp nhau.",
                        RC = 200,
                        Data = dataToTest,
                        Signature = signatureBase64
                    });
                }
                else
                {
                    return Results.Json(new
                    {
                        RM = "Thất bại! Public Key KHÔNG THỂ xác thực chữ ký được tạo bởi Private Key này.",
                        RC = -1
                    });
                }
            }
            catch (Exception ex)
            {
                // Bắt lỗi format key, lỗi mã hóa,...
                return Results.Json(new
                {
                    RM = $"Lỗi ngoại lệ trong quá trình mã hóa/giải mã: {ex.Message}",
                    RC = -500
                });
            }
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

        public IResult ChangeLastActive(long time)
        {
            if (time == null)
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

            node.LastActive = time;
     
            _nodes.Upsert(node);

            return Results.Json(new
            {
                RC = 200,
                RM = "Cập nhật lastactive thành công",
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
        
        public string GetPublicKey()
        {
            var node = _nodes.FindOne(x => x.Id == 1);
            
            if (node == null)
            {
                Console.WriteLine($"[DB] Không tìm thấy Node trong danh sách tin cậy.");
                return null;
            }

            return node.PublicKey;
        }
        public IResult PairHash(int height)
        {
            var hashpair = _chain.compareHash(height);
            return Results.Json(hashpair);
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
