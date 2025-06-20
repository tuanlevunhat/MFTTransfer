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
        private const string BlockListKeyPrefix = "blocklist:";

        public RedisService(RedisCacheHelper redisCacheHelper, ILogger<RedisService> logger)
        {
            _redisCacheHelper = redisCacheHelper ?? throw new ArgumentNullException(nameof(redisCacheHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task AddBlockAsync(string fileId, string blockId)
        {
            var blockList = await this.GetBlockListAsync(fileId);
            blockList.Add(blockId);

            await _redisCacheHelper.SetAsync($"{BlockListKeyPrefix}{fileId}", blockList);
            _logger.LogDebug("Added block {BlockId} to fileId {FileId}", blockId, fileId);
        }

        public async Task<List<string>> GetBlockListAsync(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task SaveTransferStatusAsync(string fileId, TransferStatus status)
        {
            await _redisCacheHelper.SetAsync($"{fileId}", status);
        }

        public async Task<TransferStatus> GetTransferStatusAsync(string fileId)
        {
            return await _redisCacheHelper.GetAsync<TransferStatus>($"{fileId}") ?? new TransferStatus();
        }

        public async Task ClearStateAsync(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            await _redisCacheHelper.DeleteAsync(key);
        }

        public async Task SaveMetadataAsync(string fileId, FileMetadata metadata)
        {
            var key = $"file:{fileId}:metadata";
            await _redisCacheHelper.SetAsync(key, metadata);
        }

        public async Task SetProcessedChunksAsync(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:nodeId:{nodeId}:processed_chunks";
            var increment = await GetProcessedChunksAsync(fileId, nodeId);
            await _redisCacheHelper.SetAsync(key, ++increment);
            _logger.LogDebug("Incremented processed chunks for fileId {FileId}", fileId);
        }

        public async Task<int> GetProcessedChunksAsync(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:nodeId:{nodeId}:processed_chunks";
            return await _redisCacheHelper.GetAsync<int>(key);
        }

        public async Task SetTotalChunksAsync(string fileId, int total)
        {
            var key = $"file:{fileId}:total_chunks";
            await _redisCacheHelper.SetAsync(key, total);
            _logger.LogDebug("Set total chunks to {Total} for fileId {FileId}", total, fileId);
        }

        public async Task<int> GetTotalChunksAsync(string fileId)
        {
            var key = $"file:{fileId}:total_chunks";
            var totalChunks = await _redisCacheHelper.GetAsync<int>(key);
            _logger.LogDebug("Get total chunks to {Total} for fileId {FileId}", totalChunks, fileId);

            return totalChunks;
        }

        public async Task MarkUploadChunkFailedAsync(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            var failedChunks = await GetUploadFailedChunksAsync(fileId);
            failedChunks.Add(chunkId);
            await _redisCacheHelper.SetAsync(key, failedChunks);
            _logger.LogWarning("❌ Marked chunk {ChunkId} as failed for file {FileId}", chunkId, fileId);
        }

        public async Task<List<string>> GetUploadFailedChunksAsync(string fileId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task ClearUploadFailedChunksAsync(string fileId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            await _redisCacheHelper.DeleteAsync(key);
            _logger.LogInformation("✅ Cleared failed chunks for file {FileId}", fileId);
        }

        public async Task RemoveUploadChunkFromFailedAsync(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:failed_upload_chunks";
            var failedChunks = await GetUploadFailedChunksAsync(fileId);
            failedChunks.Remove(chunkId);
            await _redisCacheHelper.SetAsync(key, failedChunks);
            _logger.LogWarning("❌ Remove failed chunk {ChunkId} for file {FileId}", chunkId, fileId);
        }

        public async Task<List<string>> GetUploadedBlockIdsAsync(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task<int> CountUploadedBlocksAsync(string fileId)
        {
            var key = $"file:{fileId}:blocks";
            return await _redisCacheHelper.GetAsync<int>(key);
        }

        public async Task SaveChunkMetadataAsync(string fileId, string chunkId, ChunkMetadata metadata)
        {
            var key = $"file:{fileId}:chunkMetadata:{chunkId}";
            await _redisCacheHelper.SetAsync(key, metadata);
        }

        public async Task<ChunkMetadata> GetChunkMetadataAsync(string fileId, string chunkId)
        {
            var key = $"file:{fileId}:chunkMetadata:{chunkId}";
            return await _redisCacheHelper.GetAsync<ChunkMetadata>(key) ?? new ChunkMetadata();
        }

        public async Task<bool> IsChunkProcessedAsync(string fileId, string chunkId, string nodeId)
        {
            var key = $"processed:{fileId}:{nodeId}:{chunkId}";
            return await _redisCacheHelper.GetAsync<bool>(key);
        }

        public async Task MarkChunkProcessedAsync(string fileId, string chunkId, string nodeId)
        {
            var key = $"processed:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.SetAsync(key, true);
        }

        public async Task MarkDownloadChunkFailedAsync(string fileId, string nodeId, string chunkId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            var downloadChunkFaileds = await GetDownloadChunkFailedAsync(fileId, nodeId);
            if (!downloadChunkFaileds.Contains(chunkId))
            {
                downloadChunkFaileds.Add(chunkId);
                await _redisCacheHelper.SetAsync<List<string>>(key, downloadChunkFaileds);
            }
        }

        public async Task<bool> IsDownloadChunkFailedAsync(string fileId, string nodeId, string chunkId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            return (await GetDownloadChunkFailedAsync(fileId, nodeId)).Contains(chunkId);
        }

        public async Task<List<string>> GetDownloadChunkFailedAsync(string fileId, string nodeId)
        {
            var key = $"download:failed:{fileId}:{nodeId}";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task SetChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId, long offset)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.SetAsync<long>(key, offset);
        }

        public async Task<long?> GetChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            return await _redisCacheHelper.GetAsync<long>(key);
        }

        public async Task ClearChunkResumeOffsetAsync(string fileId, string nodeId, string chunkId)
        {
            var key = $"resumable:{fileId}:{nodeId}:{chunkId}";
            await _redisCacheHelper.DeleteAsync(key);
        }

        public async Task<List<string>> GetAllResumableChunksAsync(string fileId, string nodeId)
        {
            var pattern = $"resumable:{fileId}:{nodeId}:*";
            return await _redisCacheHelper.GetPatternAsync(pattern);
        }

        public async Task<List<string>> GetFilesWithResumableChunksAsync(string nodeId)
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

        public async Task SetTransferNodeAsync(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:transferNode";
            await _redisCacheHelper.SetAsync(key, nodeId);
        }

        public async Task<string> GetTransferNodeAsync(string fileId)
        {
            var key = $"file:{fileId}:transferNode";
            return await _redisCacheHelper.GetAsync<string>(key) ?? string.Empty;
        }

        public async Task SetReceivingNodesAsync(string fileId, List<string> nodes)
        {
            var key = $"file:{fileId}:receivingNodes";
            await _redisCacheHelper.SetAsync(key, nodes);
        }

        public async Task<List<string>> GetReceivingNodesAsync(string fileId)
        {
            var key = $"file:{fileId}:receivingNodes";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task MarkNodeTransferCompleteAsync(string fileId, string nodeId)
        {
            var key = $"file:{fileId}:completeNodes";
            var receivingNodes = await GetTransferCompleteNodesAsync(fileId);
            receivingNodes.Add(nodeId);
            await _redisCacheHelper.SetAsync(key, receivingNodes);
        }

        public async Task<List<string>> GetTransferCompleteNodesAsync(string fileId)
        {
            var key = $"file:{fileId}:completeNodes";
            return await _redisCacheHelper.GetAsync<List<string>>(key) ?? new List<string>();
        }

        public async Task CleanUpCacheFileAsync(string fileId)
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
