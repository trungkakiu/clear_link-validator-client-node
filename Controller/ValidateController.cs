using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WorkerService1.Model;
using WorkerService1.Services.Validator_data;

namespace WorkerService1.Controller
{
    public class ValidateController
    {
        private readonly LevelDbStorage _storage;
        private readonly PeerService _peers;
        private readonly BlockchainService _chain;
        private readonly NodeConfigDto _config;
        private readonly ConsensusService _consensus;

        public ValidateController(
            LevelDbStorage storage,
            PeerService peers,
            BlockchainService chain,
            ConsensusService consensus,
            NodeConfigDto config
        )
        {
            _storage = storage;
            _peers = peers;
            _config = config;
            _chain = chain;
            _consensus = consensus;
        }

        public ApiResponse RePairProduct(RepairBlockPayloadDto dto)
        {
            try
            {
                if (dto == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto!" };


                var current_block = _chain.GetBlockByType("product_create", dto.payload.item_id, "active");
                if (current_block == null)
                {
                    return new ApiResponse { RC = 203, RM = "Missing block in! " + _config.NodeId};
                }

                var latest = _chain.GetLatestBlock();

                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";

                var block = OverrideBlockFromPayload(current_block ,dto, height, previousHash, "product_create");

                block.ValidatorSignature = SignBlock(block);

                _chain.UpdateBlockStatus(current_block, "drop");

                _chain.SaveBlock(block);

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Pair user → BLOCK CREATED",
                    RD = new
                    {
                        block_hash = block.Hash,
                        height = block.Height,
                        block_status = block.status,
                        previous = block.PreviousHash,
                        validator = _config.NodeId
                    }
                };

            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    RC = 500,
                    RM = "Internal error",
                    RD = ex.Message
                };

            }
        }

        public ApiResponse GetDropVote(VoteDropProductDto dto)
        {
            try
            {
                var results = new List<VoteDropResult>();

                foreach (var p in dto.products)
                {
                    if (string.IsNullOrEmpty(p.product_id))
                    {
                        results.Add(new VoteDropResult
                        {
                            product_id = p.product_id,
                            approve = false,
                            reason = "Invalid product_id"
                        });
                        continue;
                    }

                    var block = _chain.GetBlockByType(
                        type: "product_create",
                        currentId: p.product_id,
                        status: "active"
                    );



                    if (block == null)
                    {
                        results.Add(new VoteDropResult
                        {
                            product_id = p.product_id,
                            approve = false,
                            reason = "Block not found"
                        });
                        continue;
                    }


                    var recomputedHash = ComputeBlockHash(block.headerRaw);


                    if (recomputedHash != block.Hash)
                    {
                        results.Add(new VoteDropResult
                        {
                       
                            product_id = block.Hash,
                            //product_id = p.product_id,
                            approve = false,
                            reason = recomputedHash
                        });
                        continue;
                    }

                    results.Add(new VoteDropResult
                    {
                        product_id = p.product_id,
                        approve = true
                    });
                }

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Vote OK",
                    RD = new
                    {
                        votes = results
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    RC = 500,
                    RM = "Internal error",
                    RD = ex.Message
                };
            }
        }

        public ApiResponse PairProduct(PairProductPayloadDto dto)
        {
            try
            {    
                if (dto == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto!" };


                var latest = _chain.GetLatestBlock();
                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";
                var block = CreateProductFromPayload(dto, height, previousHash);

                block.ValidatorSignature = SignBlock(block);

                _chain.SaveBlock(block);

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Pair product → BLOCK CREATED",
                    RD = new
                    {
                        block_hash = block.Hash,
                        height = block.Height,
                        previous = block.PreviousHash,
                        type = "client",
                        validator = _config.NodeId
                    }
                };

            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    RC = 500,
                    RM = "Internal error",
                    RD = ex.Message
                };
            }
        }
        public VoteResultDto GetFirstVote(VotePayloadDto dto)
        {
            try
            {
                byte[] clientHashBytes = Encoding.UTF8.GetBytes(dto.client_hash);

                using var rsaUser = RSA.Create();
                rsaUser.ImportFromPem(dto.Public_key.AsSpan());

                var userSig = Convert.FromBase64String(dto.Signature);

                bool signatureValid = rsaUser.VerifyData(
                    clientHashBytes,
                    userSig,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                if (!signatureValid)
                {
                    return new VoteResultDto
                    {
                        ok = false,
                        signature = "",
                        payload = dto.client_hash,
                        error = "Invalid user signature"
                    };
                }

                string privatePem = _config.private_key;

                using var rsaNode = RSA.Create();
                rsaNode.ImportFromPem(privatePem.AsSpan());

                byte[] nodeSigBytes = rsaNode.SignData(
                    clientHashBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                string nodeSignature = Convert.ToBase64String(nodeSigBytes);


                return new VoteResultDto
                {
                    ok = true,
                    signature = nodeSignature,
                    payload = dto.client_hash,
                    error = ""
                };
            }
            catch (Exception ex)
            {
                return new VoteResultDto
                {
                    ok = false,
                    signature = "",
                    payload = dto.client_hash,
                    error = ex.Message
                };
            }
        }

        public VoteResultDto GetVote(VotePayloadDto dto)
        {
            try
            {
            
                byte[] clientHashBytes = Encoding.UTF8.GetBytes(dto.client_hash);

             
                using var rsaUser = RSA.Create();
                rsaUser.ImportFromPem(dto.Public_key.AsSpan());

                var userSig = Convert.FromBase64String(dto.Signature);

                bool signatureValid = rsaUser.VerifyData(
                    clientHashBytes,
                    userSig,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                if (!signatureValid)
                {
                    return new VoteResultDto
                    {
                        ok = false,
                        signature = "",
                        payload = dto.client_hash,
                        error = "Invalid user signature"
                    };
                }

              
                var current_block = _chain.GetBlockByType(dto.type, dto.current_id, dto.status);

                if (current_block == null)
                {
                    return new VoteResultDto
                    {
                        ok = false,
                        signature = "",
                        payload = dto.client_hash,
                        error = "Block not found"
                    };
                }

                
                if (current_block.Hash != dto.client_hash)
                {
                    return new VoteResultDto
                    {
                        ok = false,
                        signature = "",
                        payload = dto.client_hash,
                        error = "Block hash mismatch"
                    };
                }

              
                string privatePem = _config.private_key;

                using var rsaNode = RSA.Create();
                rsaNode.ImportFromPem(privatePem.AsSpan());

                byte[] nodeSigBytes = rsaNode.SignData(
                    clientHashBytes,           
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                );

                string nodeSignature = Convert.ToBase64String(nodeSigBytes);

                
                return new VoteResultDto
                {
                    ok = true,
                    signature = nodeSignature,
                    payload = dto.client_hash,  
                    error = ""
                };
            }
            catch (Exception ex)
            {
                return new VoteResultDto
                {
                    ok = false,
                    signature = "",
                    payload = dto.client_hash,
                    error = ex.Message
                };
            }
        }


        public ApiResponse PairUser(PairUserPayloadDto dto)
        {
            try
            {
                if (dto == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto!" };

           
                var latest = _chain.GetLatestBlock();

                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";

                var block = CreateBlockFromPayload(dto, height, previousHash);
                
                block.ValidatorSignature = SignBlock(block);

                _chain.SaveBlock(block);

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Pair user → BLOCK CREATED",
                    RD = new
                    {
                        block_hash = block.Hash,
                        ok = true,
                        type = "user",
                        height = block.Height,
                        previous = block.PreviousHash,
                        validator = _config.NodeId
                    }
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    RC = 500,
                    RM = "Internal error",
                    RD = ex.Message
                };
            }
        }

        public Block CreateBlockFromPayload(PairUserPayloadDto payload, int height, string previousHash)
        {
            if (payload == null || payload.user == null)
                throw new Exception("Payload or payload.user is null.");

            var merkle = payload.user.hash;
            string headerRaw =
            $"{height}|{previousHash}|{""}|{payload.user.id}|{payload.user.version}|{payload.user.type}|{payload.user.hash}";

            var block = new Block
            {
                headerRaw = headerRaw,
                Height = height,
                PreviousHash = previousHash,
                type = payload.user.type,
                current_id = "",
                Owner_id = payload.user.id,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.user.version,
            };

            block.Hash = ComputeBlockHash(headerRaw);

            return block;
        }

        public Block CreateProductFromPayload(PairProductPayloadDto payload, int height, string previousHash)
        {
            if (payload == null )
                throw new Exception("Payload product is null.");

            if(payload.payload == null)
            {
                throw new Exception("Payload.payload is null.");

            }
            var merkle = payload.payload.hash;
            string headerRaw =
            $"{height}|{previousHash}|{payload.payload.product_id}|{payload.payload.Owner_id}|{payload.payload.version}|{payload.payload.type}|{payload.payload.hash}";


            var block = new Block
            {
                headerRaw = headerRaw,
                Height = height,
                PreviousHash = previousHash,
                type = payload.payload.type,
                current_id = payload.payload.product_id,
                Owner_id = payload.payload.Owner_id,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.payload.version,
             
            };

            block.Hash = ComputeBlockHash(headerRaw);

            return block;
        }

        public Block OverrideBlockFromPayload(Block? Current_block ,RepairBlockPayloadDto payload, int height, string previousHash, string type)
        {
            if (payload == null)
                throw new Exception("Payload override is null.");

            if (payload.payload == null)
            {
                throw new Exception("Payload.payload is null.");
            }

            if (Current_block  == null)
            {
                throw new Exception("Missing block");
            }


            var merkle = payload.payload.hash;

            var block = new Block
            {   
                headerRaw = Current_block.headerRaw,
                Height = height,
                PreviousHash = previousHash,
                type = type,
                current_id = payload.payload.item_id,
                Owner_id = payload.payload.Owner_id,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.payload.version,
            };

            block.Hash = ComputeBlockHash(Current_block.headerRaw);

            return block;
        }

        private string ComputeBlockHash(string raw)
        {
            using var sha = SHA256.Create();
            return Convert
                .ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)))
                .ToLowerInvariant();
        }



        private string SignBlock(Block block)
        {
            string json = JsonSerializer.Serialize(block);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(_config.private_key.AsSpan());

            return Convert.ToBase64String(
                rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            );
        }

      
    }
}
