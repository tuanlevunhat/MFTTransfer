using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Helpers;
using MFTTransfer.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

public class InitTransferHandler : IKafkaMessageHandler
{
    private readonly BlobStorageHelper _blobStorageHelper;
    private readonly IHttpClientFactory _httpClientFactory;
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
        _blobStorageHelper = blobStorageHelper;
        _httpClientFactory = httpClientFactory;
        _redisService = redisService;
        _kafkaService = kafkaService;
        _blobService = blobService;
        _configuration = configuration;
        _logger = logger;
    }

    public string Topic => KafkaConstant.KafkaFileTransferInitTopic;
    public string GroupId => KafkaConstant.KafkaGroupId;

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

        await _redisService.SetReceivingNodes(init.FileId, init.ReceivedNodes);
        const int chunkSize = 10 * 1024 * 1024;
        var totalChunks = (int)Math.Ceiling((double)init.Size / chunkSize);
        await _redisService.SetTotalChunks(init.FileId, totalChunks);
        _logger.LogInformation("📂 Starting parallel chunk upload for file {FileId}, totalChunks: {Count}", init.FileId, totalChunks);

        var uploadedChunks = new List<ChunkMetadata>();
        var semaphore = new SemaphoreSlim(4); // ✅ Limit concurrency
        var tasks = new List<Task>();

        using var fileStream = new FileStream(init.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        for (int i = 0; i < totalChunks; i++)
        {
            var chunkIndex = i;
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(() =>
                UploadChunkAsync(fileStream, init.FileId, init.FullName, chunkIndex, chunkSize, uploadedChunks, semaphore, cancellationToken),
                cancellationToken));
        }

        await Task.WhenAll(tasks);

        //Check if any chunks failed during upload to blob, retry upload.
        var failedChunks = await _redisService.GetUploadFailedChunks(init.FileId);
        if (failedChunks.Any())
        {
            _logger.LogWarning("🔁 Retrying {Count} failed chunks for file {FileId}...", failedChunks.Count, init.FileId);
            var retryTasks = new List<Task>();

            foreach (var chunkId in failedChunks)
            {
                if (!int.TryParse(chunkId.Replace("chunk_", ""), out int chunkIndex))
                {
                    _logger.LogWarning("⚠️ Invalid chunkId format: {ChunkId}", chunkId);
                    continue;
                }

                await semaphore.WaitAsync(cancellationToken);
                retryTasks.Add(Task.Run(() =>
                    UploadChunkAsync(fileStream, init.FileId, init.FullName, chunkIndex, chunkSize, uploadedChunks, semaphore, cancellationToken),
                    cancellationToken));
            }

            await Task.WhenAll(retryTasks);
        }

        if (uploadedChunks.Count == 0)
        {
            _logger.LogError("❌ No chunks uploaded successfully for file {FileId}", init.FileId);
            return;
        }

        _logger.LogInformation("✅ Uploaded {Count} chunks for file {FileId}. Sending chunk messages to nodes...", uploadedChunks.Count, init.FileId);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 10 // Tune based on CPU/network capacity
        };

        await Parallel.ForEachAsync(uploadedChunks, parallelOptions, async (chunk, chunkToken) =>
        {
            foreach (var node in init.ReceivedNodes)
            {
                var topic = GetTopicForNode(node);
                if (string.IsNullOrWhiteSpace(topic))
                {
                    _logger.LogWarning("❌ Node {Node} has no configured topic", node);
                    continue;
                }

                // Check if already processed
                var processed = await _redisService.IsChunkProcessed(init.FileId, chunk.ChunkId, node);
                if (processed)
                {
                    _logger.LogInformation("🔁 Chunk {ChunkId} already sent to node {Node}, skipping.", chunk.ChunkId, node);
                    continue;
                }

                try
                {
                    await _kafkaService.SendChunkMessageToNode(topic, new ChunkMessage
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

                    await _redisService.MarkChunkProcessed(init.FileId, chunk.ChunkId, node);

                    _logger.LogInformation("📤 Sent chunk {ChunkId} to node {Node} via topic {Topic}", chunk.ChunkId, node, topic);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to send chunk {ChunkId} to node {Node}", chunk.ChunkId, node);
                }
            }
        });

        _logger.LogInformation("🎉 Completed upload and dispatch for file {FileId}", init.FileId);
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

            var chunkMetadata = await _blobService.UploadBlobPerChunk(ms, fileId, chunkId);
            if (chunkMetadata != null)
            {
                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId));
                await _redisService.AddBlock(fileId, blockId);

                chunkMetadata.FileId = fileId;
                chunkMetadata.FullFileName = fullFileName;
                chunkMetadata.TotalChunks = await _redisService.GetTotalChunks(fileId);
                chunkMetadata.ChunkIndex = chunkIndex;
                await _redisService.SaveChunkMetadata(fileId, chunkId, chunkMetadata);

                lock (uploadedChunks)
                {
                    uploadedChunks.Add(chunkMetadata);
                }

                return true;
            }
            else
            {
                _logger.LogError("❌ Upload failed for chunk {ChunkId}", chunkId);
                await _redisService.MarkUploadChunkFailed(fileId, chunkId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Unexpected error uploading chunk {ChunkId}", chunkId);
            await _redisService.MarkUploadChunkFailed(fileId, chunkId);
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string? GetTopicForNode(string nodeId)
    {
        return _configuration[$"Kafka:Nodes:{nodeId}:ChunkProcessingTopic"];
    }
}
