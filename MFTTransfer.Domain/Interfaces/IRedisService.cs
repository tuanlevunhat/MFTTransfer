using MFTTransfer.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Interfaces
{
    public interface IRedisService
    {
        Task AddBlockAsync(string fileId, string blockId);
        Task<List<string>> GetBlockListAsync(string fileId);
        Task SaveTransferStatusAsync(string fileId, TransferStatus status);
        Task<TransferStatus> GetTransferStatusAsync(string fileId);
        Task ClearStateAsync(string fileId);
        Task SaveMetadataAsync(string fileId, FileMetadata metadata);
        Task SetProcessedChunksAsync(string fileId, string nodeId);
        Task<int> GetProcessedChunksAsync(string fileId, string nodeId);
        Task SetTotalChunksAsync(string fileId, int total);
        Task<int> GetTotalChunksAsync(string fileId);
        Task MarkUploadChunkFailedAsync(string fileId, string chunkId);
        Task<List<string>> GetUploadFailedChunksAsync(string fileId);
        Task ClearUploadFailedChunksAsync(string fileId);
        Task RemoveUploadChunkFromFailedAsync(string fileId, string chunkId);
        Task<List<string>> GetUploadedBlockIdsAsync(string fileId);
        Task<int> CountUploadedBlocksAsync(string fileId);
        Task SaveChunkMetadataAsync(string fileId, string chunkId, ChunkMetadata metadata);
        Task<ChunkMetadata> GetChunkMetadataAsync(string fileId, string chunkId);
        Task<bool> IsChunkProcessedAsync(string fileId, string chunkId, string nodeId);
        Task MarkChunkProcessedAsync(string fileId, string chunkId, string nodeId);
        Task MarkDownloadChunkFailedAsync(string fileId, string nodeId, string chunkId);
        Task<bool> IsDownloadChunkFailedAsync(string fileId, string nodeId, string chunkId);
        Task<List<string>> GetDownloadChunkFailedAsync(string fileId, string nodeId);
        Task SetChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId, long offset);
        Task<long?> GetChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId);
        Task ClearChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId);
        Task<List<string>> GetAllResumableChunksAsync(string fileId, string nodeId);
        Task<List<string>> GetFilesWithResumableChunksAsync(string nodeId);
        Task SetReceivingNodesAsync(string fileId, List<string> nodes);
        Task<List<string>> GetReceivingNodesAsync(string fileId);
        Task SetTransferNodeAsync(string fileId, string nodeId);
        Task<string> GetTransferNodeAsync(string fileId);
        Task MarkNodeTransferCompleteAsync(string fileId, string nodeId);
        Task<List<string>> GetTransferCompleteNodesAsync(string fileId);
        Task CleanUpCacheFileAsync(string fileId);
    }
}
