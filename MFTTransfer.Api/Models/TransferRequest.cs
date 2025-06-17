namespace MFTTransfer.Api.Models
{
    public class TransferRequest
    {
        public string FullPath { get; set; }

        public bool IsParallelReceive { get; set; }

        public List<string> ReceivedNodes { get; set; }
    }

    public class TransferRetryRequest
    {
        public string FullPath { get; set; } = string.Empty;
    }
}
