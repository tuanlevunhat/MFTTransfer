namespace MFTTransfer.Domain.Entities
{
    public class FileTransferInitMessage
    {
        public string TransferNode { get; set; }
        public string FileId { get; set; } = default!;
        public string FullName { get; set; }
        public string FullPath { get; set; } = default!;
        public long Size { get; set; }
        public List<string> ReceivedNodes { get; set; }
    }
}
