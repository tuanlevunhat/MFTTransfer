using System.Text;
using System.Text.Json;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MFTTransfer.BackgroundJobs.Consumer
{
    public class InitTransferHandler : IKafkaMessageHandler
    {
        private readonly IRedisService _redisService;
        private readonly IKafkaService _kafkaService;
        private readonly IBlobService _blobService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InitTransferHandler> _logger;

        public InitTransferHandler(
            BlobStorageHelper blobStorageHelper,
            IHttpClientFactory httpClientFactory,
            IRedisService redisService,
            IKafkaService kafkaService,
            IBlobService blobService,
            IConfiguration configuration,
            ILogger<InitTransferHandler> logger)
        {
            _redisService = redisService;
            _kafkaService = kafkaService;
            _blobService = blobService;
            _configuration = configuration;
            _logger = logger;
        }

        public string Topic => string.Concat(_configuration["Kafka:Topic:FileTransferInit"], "_", _configuration["NodeSettings:NodeId"]);
        public string GroupId => string.Concat(_configuration["Kafka:Group:FileTransferInit"], "_", _configuration["NodeSettings:NodeId"]);

        public async Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            var init = JsonSerializer.Deserialize<FileTransferInitMessage>(message);
            if (init == null || string.IsNullOrWhiteSpace(init.FullPath))
            {
                _logger.LogWarning("❌ Invalid transfer init message: {Message}", message);
                return;
            }

            if (!File.Exists(init.FullPath))
            {
                _logger.LogError("❌ File not found at path: {Path}", init.FullPath);
                return;
            }

            await _redisService.SetTransferNodeAsync(init.FileId, init.TransferNode);
            await _redisService.SetReceivingNodesAsync(init.FileId, init.ReceivedNodes);
            const int chunkSize = 10 * 1024 * 1024;
            var totalChunks = (int)Math.Ceiling((double)init.Size / chunkSize);
            await _redisService.SetTotalChunksAsync(init.FileId, totalChunks);
            _logger.LogInformation("📂 Starting parallel chunk upload for file {FileId}, totalChunks: {Count}", init.FileId, totalChunks);

            var uploadedChunks = new List<ChunkMetadata>();
            var chunkIndices = Enumerable.Range(0, totalChunks);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = int.Parse(_configuration["MaxDegreeOfParallelism"] ?? "4") };// Tune based on CPU/network capacity

            long fileSize = new FileInfo(init.FullPath).Length;

            await Parallel.ForEachAsync(chunkIndices, parallelOptions, async (chunkIndex, token) =>
            {
                await UploadChunkAsync(init.FullPath, init.FileId, init.FullName, chunkIndex, chunkSize, fileSize, uploadedChunks, token);
            });


            //Check if any chunks failed during upload to blob, retry upload.
            var failedChunks = await _redisService.GetUploadFailedChunksAsync(init.FileId);
            if (failedChunks.Any())
            {
                _logger.LogWarning("🔁 Retrying {Count} failed chunks for file {FileId}...", failedChunks.Count, init.FileId);
                await Parallel.ForEachAsync(failedChunks, parallelOptions, async (chunkId, token) =>
                {
                    if (!int.TryParse(chunkId.Replace("chunk_", ""), out int chunkIndex))
                    {
                        _logger.LogWarning("⚠️ Invalid chunkId format: {ChunkId}", chunkId);
                        return;
                    }

                    await UploadChunkAsync(init.FullPath, init.FileId, init.FullName, chunkIndex, chunkSize, fileSize, uploadedChunks, token);
                });
            }

            if (uploadedChunks.Count == 0)
            {
                _logger.LogError("❌ No chunks uploaded successfully for file {FileId}", init.FileId);
                return;
            }

            _logger.LogInformation("✅ Uploaded {Count} chunks for file {FileId}. Sending chunk messages to nodes...", uploadedChunks.Count, init.FileId);

            await Parallel.ForEachAsync(init.ReceivedNodes, parallelOptions, async (node, token) =>
            {
                var topic = GetChunkProcessingTopicForNode(node);
                if (string.IsNullOrWhiteSpace(topic))
                {
                    _logger.LogWarning("❌ Node {Node} has no configured topic", node);
                    return;
                }

                var unprocessedChunks = new List<ChunkMessage>();

                foreach (var chunk in uploadedChunks)
                {
                    var processed = await _redisService.IsChunkProcessedAsync(init.FileId, chunk.ChunkId, node);
                    if (processed)
                    {
                        _logger.LogInformation("🔁 Chunk {ChunkId} already sent to node {Node}, skipping.", chunk.ChunkId, node);
                        continue;
                    }

                    unprocessedChunks.Add(new ChunkMessage
                    {
                        FileId = init.FileId,
                        FullFileName = init.FullName,
                        ChunkId = chunk.ChunkId,
                        ChunkIndex = chunk.ChunkIndex,
                        BlobUrl = chunk.BlobUrl,
                        Size = chunk.Size,
                        TotalChunks = totalChunks,
                        IsLast = chunk.ChunkId == $"chunk_{totalChunks - 1}"
                    });
                }

                if (!unprocessedChunks.Any())
                {
                    _logger.LogInformation("ℹ️ No new chunks to send to node {Node}", node);
                    return;
                }

                const int maxKafkaMessageSize = 900 * 1024; // limit message in Kafka < 1MB
                var chunkBatches = new List<List<ChunkMessage>>();
                var currentBatch = new List<ChunkMessage>();
                int currentSize = 0;

                foreach (var chunk in unprocessedChunks)
                {
                    var serializedChunk = JsonSerializer.Serialize(chunk);
                    int chunkSize = Encoding.UTF8.GetByteCount(serializedChunk);
                    if (currentSize + chunkSize > maxKafkaMessageSize && currentBatch.Any())
                    {
                        chunkBatches.Add(new List<ChunkMessage>(currentBatch));
                        currentBatch.Clear();
                        currentSize = 0;
                    }

                    currentBatch.Add(chunk);
                    currentSize += chunkSize;
                }

                // Last batch
                if (currentBatch.Any())
                {
                    chunkBatches.Add(currentBatch);
                }

                foreach (var batch in chunkBatches)
                {
                    try
                    {
                        await _kafkaService.SendChunkBatchMessageAsync(topic, init.FileId, batch);

                        foreach (var chunk in batch)
                        {
                            await _redisService.MarkChunkProcessedAsync(init.FileId, chunk.ChunkId, node);
                        }

                        _logger.LogInformation("📤 Sent batch of {Count} chunks to node {Node} via topic {Topic}", batch.Count, node, topic);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to send chunk batch to node {Node}", node);
                    }
                }
            });


            _logger.LogInformation("🎉 Completed upload and dispatch for file {FileId}", init.FileId);
        }

        private async Task UploadChunkAsync(
     string filePath,
     string fileId,
     string fileName,
     int chunkIndex,
     int chunkSize,
     long fileSize,
     List<ChunkMetadata> uploadedChunks,
     CancellationToken token)
        {
            var chunkId = $"chunk_{chunkIndex}";

            try
            {
                long offset = (long)chunkIndex * chunkSize;

                if (offset >= fileSize)
                {
                    _logger.LogWarning("⚠️ Offset {Offset} is out of bounds for file {File} (size: {Size})", offset, filePath, fileSize);
                    await _redisService.MarkUploadChunkFailedAsync(fileId, chunkId);
                    return;
                }

                int sizeToRead = (int)Math.Min(chunkSize, fileSize - offset);
                var buffer = new byte[sizeToRead];

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(offset, SeekOrigin.Begin);
                await fs.ReadExactlyAsync(buffer.AsMemory(0, sizeToRead), token).ConfigureAwait(false);

                using var ms = new MemoryStream(buffer);

                var chunkMetadata = await _blobService.UploadBlobPerChunkAsync(ms, fileId, chunkId);
                if (chunkMetadata is null)
                {
                    _logger.LogError("❌ Upload failed for chunk {ChunkId}", chunkId);
                    await _redisService.MarkUploadChunkFailedAsync(fileId, chunkId);
                    return;
                }

                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId));
                await _redisService.AddBlockAsync(fileId, blockId);

                chunkMetadata.FileId = fileId;
                chunkMetadata.FullFileName = fileName;
                chunkMetadata.TotalChunks = await _redisService.GetTotalChunksAsync(fileId);
                chunkMetadata.ChunkIndex = chunkIndex;

                await _redisService.SaveChunkMetadataAsync(fileId, chunkId, chunkMetadata);

                lock (uploadedChunks)
                {
                    uploadedChunks.Add(chunkMetadata);
                }

                _logger.LogInformation("✅ Uploaded chunk {ChunkId} (offset: {Offset}, size: {Size})", chunkId, offset, sizeToRead);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error uploading chunk {ChunkId}", chunkId);
                await _redisService.MarkUploadChunkFailedAsync(fileId, chunkId);
            }
        }

        private async Task<bool> UploadChunkAsync(
            FileStream fileStream,
            string fileId,
            string fullFileName,
            int chunkIndex,
            int chunkSize,
            List<ChunkMetadata> uploadedChunks,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken)
        {
            var chunkId = $"chunk_{chunkIndex}";
            try
            {
                var buffer = new byte[chunkSize];
                int bytesRead;

                lock (fileStream)
                {
                    fileStream.Position = chunkIndex * chunkSize;
                    bytesRead = fileStream.Read(buffer, 0, chunkSize);
                }

                if (bytesRead == 0)
                {
                    _logger.LogWarning("⚠️ Skipped empty chunk {ChunkId}", chunkId);
                    return false;
                }

                var exactChunk = new byte[bytesRead];
                Array.Copy(buffer, exactChunk, bytesRead);
                using var ms = new MemoryStream(exactChunk);

                var chunkMetadata = await _blobService.UploadBlobPerChunkAsync(ms, fileId, chunkId);
                if (chunkMetadata != null)
                {
                    var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId));
                    await _redisService.AddBlockAsync(fileId, blockId);

                    chunkMetadata.FileId = fileId;
                    chunkMetadata.FullFileName = fullFileName;
                    chunkMetadata.TotalChunks = await _redisService.GetTotalChunksAsync(fileId);
                    chunkMetadata.ChunkIndex = chunkIndex;
                    await _redisService.SaveChunkMetadataAsync(fileId, chunkId, chunkMetadata);

                    lock (uploadedChunks)
                    {
                        uploadedChunks.Add(chunkMetadata);
                    }

                    return true;
                }
                else
                {
                    _logger.LogError("❌ Upload failed for chunk {ChunkId}", chunkId);
                    await _redisService.MarkUploadChunkFailedAsync(fileId, chunkId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error uploading chunk {ChunkId}", chunkId);
                await _redisService.MarkUploadChunkFailedAsync(fileId, chunkId);
                return false;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private string? GetChunkProcessingTopicForNode(string nodeId)
        {
            return string.Concat(_configuration["Kafka:Topic:ChunkProcessing"], "_", nodeId);
        }
    }
}
