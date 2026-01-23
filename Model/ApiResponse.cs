
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkerService1.Model
{
    public class ApiResponse
    {
        public string RM { get; set; } = string.Empty;
        public int RC { get; set; }                    
        public object? RD { get; set; }              
        public static IResult Ok(string message, object? data = null)
        {
            return Results.Json(new ApiResponse
            {
                RM = message,
                RC = 200,
                RD = data
            });
        }

        public static IResult Error(string message, int code = 400, object? data = null)
        {
            return Results.Json(new ApiResponse
            {
                RM = message,
                RC = code,
                RD = data
            });
        }
    }
}


namespace WorkerService1.Model
{

    public class VoteDropProductDto {

        public List<ProductVoteItem> products { get; set; } = new();
    }

    public class ProductVoteItem
    {
        public string product_id { get; set; } = string.Empty;
    }

    public class VotePayloadDto
    {
        public string client_hash { get; set; }

        public string Signature { get; set; }

        public string Public_key { get; set; }
        public string voteRoundId { get; set; }

        public string current_id { get; set; }
        public string type { get; set; }
        public string status { get; set; }
        public string command_type { get; set; }
    }

    public class VoteDropResult
    {
        public string product_id { get; set; } = string.Empty;
        public bool approve { get; set; }
        public string? reason { get; set; }
    }

    public class VoteResultDto
    {
        public object payload { get; set; }
        public string signature { get; set; }
        public string error { get; set; }
        public bool ok { get; set; }
    }

    public class PairProductPayloadDto
    {
        public string timestamp { get; set; }
        public ProductPairPayload payload { get; set; }
    }


    public class ProductPairPayload
    {
        public string hash { get; set; }
        public string type { get; set; }
        public string version { get; set; }
        public string product_id { get; set; }
        public string Owner_id { get; set; }
        public string detail { get; set; }
        public string status { get; set; }
        public string original_value { get; set; }

    }

    public class RepairBlockPayloadDto
    {
        public string timestamp { get; set; }
        public ItemRepairPayload payload { get; set; }
    }

    public class ItemRepairPayload
    {
  
        public string item_id {  get; set; }
        public string hash { get; set; }
        public string version { get; set; }
        public string Owner_id { get; set; }
        public string detail { get; set; }
        public string status { get; set; }
        public string first_price { get; set; }
        public string type { get; set; }

    }

    public class PairUserPayloadDto
    {
    
        public string timestamp { get; set; }
        public PairUserHash user { get; set; }
    }

    public class PairUserHash
    {
        public string id { get; set; }
        public string hash { get; set; }
        public string type { get; set; }
        public string version { get; set; }
    }

    public class UserPairPayloadDto
    {
        public UserPairInnerPayload? Payload { get; set; }
        public string? PublicKey { get; set; }
        public string? Signature { get; set; }
    }

    public class UserPairInnerPayload
    {
        public long? Timestamp { get; set; }
        public List<UserItemDto>? Users { get; set; }
    }

    public class UserItemDto
    {
        public string user_id { get; set; }
        public string? Hash { get; set; }
    }

    public class AdminChangeStatusPayloadDto
    {
        public AdminChangeStatusInner? Payload { get; set; }
        public string? Signature { get; set; }
        public string? PublicKey { get; set; }
    }


    public class RepairBlockDto
    {
        public string type { get; set; }
        public UserPairPayloadDto payload { get; set; }
    }
    public class AdminChangeStatusInner
    {
        public string? Node_Target_Address { get; set; }
        public string? Status { get; set; }
    }
}
