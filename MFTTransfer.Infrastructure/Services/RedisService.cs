using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace MFTTransfer.Infrastructure.Services
{

    public class RedisService : IRedisService
    {
        private readonly RedisCacheHelper _redisCacheHelper;
        private readonly ILogger<RedisService> _logger;
        private const string ProcessedChunksKeyPrefix = "processed-chunks-";
        private const string TotalChunksKeyPrefix = "total-chunks-";
        private const string BlockListKeyPrefix = "blocklist:";

        public RedisService(RedisCacheHelper redisCacheHelper, ILogger<RedisService> logger)
        {
            _redisCacheHelper = redisCacheHelper ?? throw new ArgumentNullException(nameof(redisCacheHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task AddBlock(string fileId, string blockId)
        {
            var blockList = await this.GetBlockList(fileId);
            blockList.Add(blockId);

            await _redisCacheHelper.SetAsync($"{BlockListKeyPrefix}{fileId}", blockList);
            _logger.LogDebug("Added block {BlockId} to fileId {FileId}", blockId, fileId);
        }

        public async Task<List<string>> GetBlockList(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task SaveTransferStatus(string fileId, TransferStatus status)
        {
            await _redisCacheHelper.SetAsync($"{fileId}", status);
        }

        public async Task<TransferStatus> GetTransferStatus(string fileId)
        {
            return await _redisCacheHelper.GetAsync<TransferStatus>($"{fileId}") ?? new TransferStatus();
        }

        public async Task ClearState(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            await _redisCacheHelper.DeleteAsync(key);
        }

        public async Task SaveMetadata(string fileId, FileMetadata metadata)
        {
            var key = $"file:{fileId}:metadata";
            await _redisCacheHelper.SetAsync(key, metadata);
        }

        public async Task SetProcessedChunks(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:nodeId:{nodeId}:processed_chunks";
            var increment = await GetProcessedChunks(fileId, nodeId);
            await _redisCacheHelper.SetAsync(key, ++increment);
            _logger.LogDebug("Incremented processed chunks for fileId {FileId}", fileId);
        }

        public async Task<int> GetProcessedChunks(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:nodeId:{nodeId}:processed_chunks";
            return await _redisCacheHelper.GetAsync<int>(key);
        }

        public async Task SetTotalChunks(string fileId, int total)
        {
            var key = $"file:{fileId}:total_chunks";
            await _redisCacheHelper.SetAsync(key, total);
            _logger.LogDebug("Set total chunks to {Total} for fileId {FileId}", total, fileId);
        }

        public async Task<int> GetTotalChunks(string fileId)
        {
            var key = $"file:{fileId}:total_chunks";
            var totalChunks = await _redisCacheHelper.GetAsync<int>(key);
            _logger.LogDebug("Get total chunks to {Total} for fileId {FileId}", totalChunks, fileId);

            return totalChunks;
        }

        public async Task MarkUploadChunkFailed(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            var failedChunks = await GetUploadFailedChunks(fileId);
            failedChunks.Add(chunkId);
            await _redisCacheHelper.SetAsync(key, failedChunks);
            _logger.LogWarning("❌ Marked chunk {ChunkId} as failed for file {FileId}", chunkId, fileId);
        }

        public async Task<List<string>> GetUploadFailedChunks(string fileId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task ClearUploadFailedChunks(string fileId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            await _redisCacheHelper.DeleteAsync(key);
            _logger.LogInformation("✅ Cleared failed chunks for file {FileId}", fileId);
        }

        public async Task RemoveUploadChunkFromFailed(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            var failedChunks = await GetUploadFailedChunks(fileId);
            failedChunks.Remove(chunkId);
            await _redisCacheHelper.SetAsync(key, failedChunks);
            _logger.LogWarning("❌ Remove failed chunk {ChunkId} for file {FileId}", chunkId, fileId);
        }

        public async Task<List<string>> GetUploadedBlockIds(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task<int> CountUploadedBlocks(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<int>(key);
        }

        public async Task SaveChunkMetadata(string fileId, string chunkId, ChunkMetadata metadata)
        {
            var key = $"file:{fileId}:chunkMetadata:{chunkId}";
            await _redisCacheHelper.SetAsync(key, metadata);
        }

        public async Task<ChunkMetadata> GetChunkMetadata(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:chunkMetadata:{chunkId}";
            return await _redisCacheHelper.GetAsync<ChunkMetadata>(key) ?? new ChunkMetadata();
        }

        public async Task<bool> IsChunkProcessed(string fileId, string chunkId, string nodeId)
        {
            var key = $"processed:{fileId}:{nodeId}:{chunkId}";
            return await _redisCacheHelper.GetAsync<bool>(key);
        }

        public async Task MarkChunkProcessed(string fileId, string chunkId, string nodeId)
        {
            var key = $"processed:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.SetAsync(key, true);
        }

        public async Task MarkDownloadChunkFailed(string fileId, string nodeId, string chunkId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            var downloadChunkFaileds = await GetDownloadChunkFailed(fileId, nodeId);
            if (!downloadChunkFaileds.Contains(chunkId))
            {
                downloadChunkFaileds.Add(chunkId);
                await _redisCacheHelper.SetAsync<List<string>>(key, downloadChunkFaileds);
            }
        }

        public async Task<bool> IsDownloadChunkFailed(string fileId, string nodeId, string chunkId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            return (await GetDownloadChunkFailed(fileId, nodeId)).Contains(chunkId);
        }

        public async Task<List<string>> GetDownloadChunkFailed(string fileId, string nodeId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task SetChunkResumeOffset(string fileId, string nodeId, string chunkId, long offset)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.SetAsync<long>(key, offset);
        }

        public async Task<long?> GetChunkResumeOffset(string fileId, string nodeId, string chunkId)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            return await _redisCacheHelper.GetAsync<long>(key);
        }

        public async Task ClearChunkResumeOffset(string fileId, string nodeId, string chunkId)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.DeleteAsync(key);
        }

        public async Task<List<string>> GetAllResumableChunks(string fileId, string nodeId)
        {
            var pattern = $"resumable:{fileId}:{nodeId}:*";
            return await _redisCacheHelper.GetPatternAsync(pattern);
        }

        public async Task<List<string>> GetFilesWithResumableChunks(string nodeId)
        {
            var pattern = $"resumable:*:{nodeId}:*";

            var fileIds = new HashSet<string>();
            var keys = await _redisCacheHelper.GetKeysByPatternAsync(pattern);
            foreach (var key in keys)
            {
                var parts = key.ToString().Split(':'); // ["resumable", "<fileId>", "<nodeId>", "<chunkId>"]
                if (parts.Length >= 4)
                {
                    fileIds.Add(parts[1]); // <fileId>
                }
            }

            return fileIds.ToList();
        }

        public async Task SetReceivingNodes(string fileId, List<string> nodes)
        {
            var key = $"file:{fileId}:receivingNodes";
            await _redisCacheHelper.SetAsync(key, nodes);
        }

        public async Task<List<string>> GetReceivingNodes(string fileId)
        {
            var key = $"file:{fileId}:receivingNodes";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task MarkNodeTransferComplete(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:completeNodes";
            var receivingNodes = await GetTransferCompleteNodes(fileId);
            receivingNodes.Add(nodeId);
            await _redisCacheHelper.SetAsync(key, receivingNodes);
        }

        public async Task<List<string>> GetTransferCompleteNodes(string fileId)
        {
            var key = $"file:{fileId}:completeNodes";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task CleanUpCacheFile(string fileId)
        {
            var pattern = $"*{fileId}*";
            var keys = await _redisCacheHelper.GetKeysByPatternAsync(pattern);
            foreach (var key in keys)
            {
                await _redisCacheHelper.DeleteAsync(key);
            }
        }
    }
}
