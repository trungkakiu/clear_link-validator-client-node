namespace WorkerService1.Model
{
    public class NodeInfo
    {
        public int Id { get; set; }

        public string NodeId { get; set; }  
        public string Role { get; set; } = "validator";

        public string Address { get; set; } 
        public string IpAddress { get; set; }

        public string PublicKey { get; set; }

        public double Stake { get; set; } = 0;
        public double ReputationScore { get; set; } = 100;

        public string Status { get; set; } = "active";
        public string node_type { get; set; } = "user_validator";

        public int BlockHeight { get; set; } = 0;

        public float? PingLatency { get; set; }
        public float? NetworkSpeed { get; set; }
        public float? StorageUsage { get; set; }
        public float? CpuUsage { get; set; }
        public float? MemoryUsage { get; set; }

        public long LastActive { get; set; } =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
