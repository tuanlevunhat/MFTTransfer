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
        Task AddBlock(string fileId, string blockId);
        Task<List<string>> GetBlockList(string fileId);
        Task SaveTransferStatus(string fileId, TransferStatus status);
        Task<TransferStatus> GetTransferStatus(string fileId);
        Task ClearState(string fileId);
        Task SaveMetadata(string fileId, FileMetadata metadata);
        Task SetProcessedChunks(string fileId, string nodeId);
        Task<int> GetProcessedChunks(string fileId, string nodeId);
        Task SetTotalChunks(string fileId, int total);
        Task<int> GetTotalChunks(string fileId);
        Task MarkUploadChunkFailed(string fileId, string chunkId);
        Task<List<string>> GetUploadFailedChunks(string fileId);
        Task ClearUploadFailedChunks(string fileId);
        Task RemoveUploadChunkFromFailed(string fileId, string chunkId);
        Task<List<string>> GetUploadedBlockIds(string fileId);
        Task<int> CountUploadedBlocks(string fileId);
        Task SaveChunkMetadata(string fileId, string chunkId, ChunkMetadata metadata);
        Task<ChunkMetadata> GetChunkMetadata(string fileId, string chunkId);
        Task<bool> IsChunkProcessed(string fileId, string chunkId, string nodeId);
        Task MarkChunkProcessed(string fileId, string chunkId, string nodeId);
        Task MarkDownloadChunkFailed(string fileId, string nodeId, string chunkId);
        Task<bool> IsDownloadChunkFailed(string fileId, string nodeId, string chunkId);
        Task<List<string>> GetDownloadChunkFailed(string fileId, string nodeId);
        Task SetChunkResumeOffset(string fileId, string nodeId, string chunkId, long offset);
        Task<long?> GetChunkResumeOffset(string fileId, string nodeId, string chunkId);
        Task ClearChunkResumeOffset(string fileId, string nodeId, string chunkId);
        Task<List<string>> GetAllResumableChunks(string fileId, string nodeId);
        Task<List<string>> GetFilesWithResumableChunks(string nodeId);
        Task SetReceivingNodes(string fileId, List<string> nodes);
        Task<List<string>> GetReceivingNodes(string fileId);
        Task MarkNodeTransferComplete(string fileId, string nodeId);
        Task<List<string>> GetTransferCompleteNodes(string fileId);
        Task CleanUpCacheFile(string fileId);
    }
}
