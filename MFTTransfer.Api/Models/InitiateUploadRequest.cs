namespace MFTTransfer.Api.Models
{
    public class InitiateUploadRequest
    {
        public string FileId { get; set; }
        public string FileUrl { get; set; }
    }
}
