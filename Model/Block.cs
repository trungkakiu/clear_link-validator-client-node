namespace WorkerService1.Model
{

    public class BlockAnchor
    {
        public int Height { get; set; }
        public string Hash { get; set; }
    }

    public class Block
    {
        public byte[] headerRaw { get; set; }
        public int Height { get; set; }            
        public string Hash { get; set; }
        public string type { get; set; }
        public string status { get; set; }
        public string PreviousHash { get; set; }      
        public string current_id { get; set; }
        public string? Timestamp { get; set; } 
        public string MerkleRoot { get; set; }       
        public string Creator { get; set; }
        public string Owner_id { get; set; }
        public string ValidatorSignature { get; set; }
       
        public string Version { get; set; } = "1.0";

    }


}
