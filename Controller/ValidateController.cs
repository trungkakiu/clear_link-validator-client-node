using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
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

        private readonly NodeDatabase _database;
        public ValidateController(
            LevelDbStorage storage,
            PeerService peers,
            NodeDatabase database,
            BlockchainService chain,
            ConsensusService consensus,
            NodeConfigDto config
        )
        {
            _storage = storage;
            _peers = peers;
            _config = config;
            _chain = chain;
            _database = database;
            _consensus = consensus;
        }

        public TraceVerificationResult VerifyTrace(PairProductPayloadDto dto)
        {
            var block = _chain.GetBlockByTypeVersion(dto.payload.type, dto.payload.product_id, dto.payload.version);
          
            if (block == null) 
            {
                return new TraceVerificationResult { 
                    ok = false, 
                    message = "Data not found in Ledger",
                    status = "not_found",
                    block = null 
                };
            }

            using var sha = SHA256.Create();
            if (dto.payload.hash != block.MerkleRoot) 
            {
                return new TraceVerificationResult { 
                    ok = false, 
                    message = "Data Tampered! (Content hash mismatch)", 
                    status = block.status,
                    block = block 
                };
            }

            string headerRawStr = $"{block.Height}|{block.PreviousHash}|{block.current_id}|{block.Owner_id}|{block.Version}|{block.type}|{dto.payload.hash}";
            byte[] headerRawBytes = Encoding.UTF8.GetBytes(headerRawStr);
            
            string reCalculatedBlockHash = _chain.ComputeBlockHashFromHeader(headerRawBytes);

            if (reCalculatedBlockHash != block.Hash) 
            {
                return new TraceVerificationResult { 
                    ok = false, 
                    message = "Block Header Integrity Violation!",
                    block = block 
                };
            }

            string publicKeyToVerify = _config.public_key;
            if (publicKeyToVerify == null)
            {
                return new TraceVerificationResult { 
                    ok = false, 
                    message = "missing publickey verify", 
                    status = "unauthorized",
                    block = block
                };
            }

            bool isSignatureValid = VerifyBlockSignature(block, publicKeyToVerify);
            if (!isSignatureValid) 
            {
                return new TraceVerificationResult { 
                    ok = false, 
                    message = "Security Violation! Invalid Digital Signature.", 
                    status = "unauthorized",
                    block = block 
                };
            }

            return new TraceVerificationResult {
                ok = true,
                message = "Verified & Audited by CLEARLINK Node",
                status = block.status,
                block = block 
            };
        } 
        
        public bool VerifyBlockSignature(Block block, string publicKeyPem)
        {
            try
            {
                if (string.IsNullOrEmpty(publicKeyPem)) return false;
                if (string.IsNullOrEmpty(block.ValidatorSignature)) return false;

                byte[] signatureBytes = Convert.FromBase64String(block.ValidatorSignature);

                string cleanHash = block.Hash.ToLower().Trim(); 

                byte[] dataBytes = Encoding.UTF8.GetBytes(cleanHash);

                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem); 

                return rsa.VerifyData(
                    dataBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,        
                    RSASignaturePadding.Pkcs1 
                );
            }
            catch (Exception ex)
            {
                return false;
            }
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


                    var recomputedHash = _chain.ComputeBlockHashFromHeader(block.headerRaw);


                    if (recomputedHash != block.Hash)
                    {
                        results.Add(new VoteDropResult
                        {
                            product_id = p.product_id,
                            approve = recomputedHash == block.Hash,
                            reason = ""
                        
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
                    RD = results
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

        public ApiResponse PairOtherBlock(PairOtherPayloadDto dto)
        {
            try
            {
                if (dto == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto!" };


                var latest = _chain.GetLatestBlock();
                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";
                var block = CreateOtherBlockFromPayload(dto, height, previousHash);

                block.ValidatorSignature = SignBlock(block);

                _chain.SaveBlock(block);

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Pair product → BLOCK CREATED",
                    RD = new
                    {
                        ok = true,
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
        public ApiResponse PairProduct(PairProductPayloadDto dto)
        {
            try
            {    
                if (dto == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto!" };

                var existSBlock = _chain.GetBlockByTypeVersion("product_create", dto.payload.product_id, dto.payload.version);
                if(existSBlock != null)
                    return new ApiResponse { RC = 204, RM = "Block version already exists!" };

                var latest = _chain.GetLatestBlock();
                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";
                var block = CreateProductFromPayload(dto, height, previousHash);

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
                        ok = true,
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

              
                var current_block = _chain.GetBlockByTypeVersion(dto.type, dto.current_id, dto.version);

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
                if (dto == null || dto.user == null)
                    return new ApiResponse { RC = 203, RM = "Missing dto or payload!" };

                var latest = _chain.GetLatestBlock();
                int height = latest == null ? 1 : latest.Height + 1;
                string previousHash = latest?.Hash ?? "GENESIS";

                var existSBlock = _chain.GetBlockByType("create_user", dto.user.id, "active");
                if(existSBlock != null)
                {
                    return new ApiResponse
                    {
                        RC = 203,
                        RM = "Pair user → BLOCK EXISTS",
                        RD = new
                        {
                            ok = true, 
                            block_hash = existSBlock.Hash,
                            type = "create_user",
                            height = existSBlock.Height,
                            previous = existSBlock.PreviousHash,
                            validator = _config.NodeId
                        }
                    };
                }

                var block = CreateUserBlockFromPayload(dto, height, previousHash);
                
                block.ValidatorSignature = SignBlock(block);
                _chain.SaveBlock(block);

                return new ApiResponse
                {
                    RC = 200,
                    RM = "Pair user → BLOCK CREATED",
                    RD = new
                    {
                        ok = true,
                        block_hash = block.Hash,
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
                    RD = new { ok = false, message = ex.Message }
                };
            }
        }
        public Block CreateUserBlockFromPayload(PairUserPayloadDto payload, int height, string previousHash)
        {
            if (payload == null || payload.user == null)
                throw new Exception("Payload or payload.user is null.");

            var merkle = payload.user.hash;
            var prev = previousHash ?? "GENESIS";
            string headerRaw =
            $"{height}|{prev}|{"_"}|{payload.user.id}|{"1"}|{payload.user.type}|{payload.user.hash}";

            byte[] headerRawBytes = Encoding.UTF8.GetBytes(headerRaw);

            var blockhash = _chain.ComputeBlockHashFromHeader(headerRawBytes);

            var block = new Block
            {
                headerRaw = headerRawBytes,
                Height = height,
                Hash = blockhash,
                PreviousHash = prev,
                type = payload.user.type,
                current_id = "",
                Owner_id = payload.user.id,
                status = "active",
                original_value = payload.user.original_value,
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.user.version,
            };

            block.Hash = _chain.ComputeBlockHashFromHeader(headerRawBytes);

            var signature = BlockchainService.SignBlockData(
                payload.timestamp,
                block.Hash,
                _config.private_key
            );

            block.ValidatorSignature = signature;

            return block;
        }
        public Block CreateOtherBlockFromPayload(PairOtherPayloadDto payload, int height, string previousHash)
        {
            if (payload == null || payload.payload == null)
                throw new Exception("Payload or payload.user is null.");

            var merkle = payload.payload.hash;
            var prev = previousHash ?? "GENESIS";
            string headerRaw =
            $"{height}|{prev}|{payload.payload.current_id}|{payload.payload.Owner_id}|{payload.payload.version}|{payload.payload.type}|{payload.payload.hash}";

            byte[] headerRawBytes = Encoding.UTF8.GetBytes(headerRaw);

            var blockhash = _chain.ComputeBlockHashFromHeader(headerRawBytes);

            var block = new Block
            {
                headerRaw = headerRawBytes,
                Height = height,
                Hash = blockhash,
                PreviousHash = prev,
                type = payload.payload.type,
                current_id = payload.payload.current_id,
                Owner_id = payload.payload.Owner_id,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                original_value = payload.payload.original_value,
                Creator = _config.NodeId,
                Version = payload.payload.version,
            };

            var signature = BlockchainService.SignBlockData(
                payload.timestamp,
                block.Hash,
                _config.private_key
            );

            block.ValidatorSignature = signature;
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
            var prev = previousHash ?? "GENESIS";
            string headerRaw =
            $"{height}|{prev}|{payload.payload.product_id}|{payload.payload.Owner_id}|{payload.payload.version}|{payload.payload.type}|{payload.payload.hash}";

            byte[] headerRawBytes = Encoding.UTF8.GetBytes(headerRaw);

            var blockhash = _chain.ComputeBlockHashFromHeader(headerRawBytes);

            var block = new Block
            {
                headerRaw = headerRawBytes,
                Height = height,
                Hash = blockhash,
                PreviousHash = prev,
                type = payload.payload.type,
                current_id = payload.payload.product_id,
                Owner_id = payload.payload.Owner_id,
                original_value = payload.payload.original_value,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.payload.version,
            };

            var signature = BlockchainService.SignBlockData(
                payload.timestamp,
                block.Hash,
                _config.private_key
            );

            block.ValidatorSignature = signature;

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


            var blockhash = _chain.ComputeBlockHashFromHeader(Current_block.headerRaw);

            var block = new Block
            {   
                headerRaw = Current_block.headerRaw,
                Height = height,
                Hash = blockhash,
                PreviousHash = previousHash,
                type = type,
                current_id = payload.payload.item_id,
                Owner_id = payload.payload.Owner_id,
                original_value = payload.payload.original_value,
                status = "active",
                Timestamp = payload.timestamp,
                MerkleRoot = merkle,
                Creator = _config.NodeId,
                Version = payload.payload.version,
            };

            var signature = BlockchainService.SignBlockData(
                payload.timestamp,
                block.Hash,
                _config.private_key
            );

            block.ValidatorSignature = signature;

            return block;
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
