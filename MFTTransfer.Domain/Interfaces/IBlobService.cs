using MFTTransfer.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace MFTTransfer.Domain.Interfaces
{
    public interface IBlobService
    {
        Task<ChunkMetadata> UploadBlobPerChunk(Stream stream, string fileId, string chunkId);
        Task<ChunkMetadata> UploadChunk(Stream stream, string fileId, string chunkId);
        Task<string> FinalizeUpload(string fileId, List<string> blockList);
        Task CommitFileBlocksAsync(string fileId);
        Task CleanUpBlobChunks(string fileId);
    }
}
