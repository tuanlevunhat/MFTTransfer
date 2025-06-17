using System;

namespace MFTTransfer.Domain
{
    public class TransferStatus
    {
        public string NodeId { get; set; } 
        public string FileId { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}