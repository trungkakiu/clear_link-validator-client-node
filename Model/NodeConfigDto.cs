using System.Text.Json.Serialization;

namespace WorkerService1.Model
{
    public class NodeConfigDto
    {
        [JsonPropertyName("node_id")]
        public string NodeId { get; set; }

        [JsonPropertyName("address")]
        public string Address { get; set; }

        [JsonPropertyName("port")]
        public string Port { get; set; }

        [JsonPropertyName("private_key")]
        public string private_key { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";

        [JsonPropertyName("Owner_actor")]
        public string Owner_actor { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("database")]
        public DatabaseConfig Database { get; set; }

        [JsonPropertyName("peers")]
        public List<string> Peers { get; set; }
    }

    public class DatabaseConfig
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("host")]
        public string Host { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }
    }
}
