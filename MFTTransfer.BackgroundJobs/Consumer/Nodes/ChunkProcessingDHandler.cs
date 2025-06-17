using System.Text;
using System.Text.Json;
using MFTTransfer.BackgroundJobs.Consumer.Nodes;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using StackExchange.Redis;

public class ChunkProcessingDHandler : IKafkaMessageHandler
{
    private readonly BlobStorageHelper _blobStorageHelper;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRedisService _redisService;
    private readonly IKafkaService _kafkaService;
    private readonly ILogger<ChunkProcessingDHandler> _logger;
    private readonly IChunkProcessingHelperFactory _chunkProcessingHelperFactory;
    private readonly IConfiguration _configuration;
    private readonly string _nodeId;
    private readonly string _tempFolder;
    private readonly string _mainFolder;
    public ChunkProcessingDHandler(
        BlobStorageHelper blobStorageHelper,
        IHttpClientFactory httpClientFactory,
        IRedisService redisService,
        IKafkaService kafkaService,
        IConfiguration configuration,
        IChunkProcessingHelperFactory chunkProcessingHelperFactory,
        ILogger<ChunkProcessingDHandler> logger)
    {
        _blobStorageHelper = blobStorageHelper;
        _httpClientFactory = httpClientFactory;
        _redisService = redisService;
        _kafkaService = kafkaService;
        _logger = logger;
        _configuration = configuration;
        _nodeId = _configuration["NodeDSettings:NodeId"] ?? string.Empty;
        _tempFolder = _configuration["NodeDSettings:TempFolder"] ?? string.Empty;
        _mainFolder = _configuration["NodeDSettings:MainFolder"] ?? string.Empty; 
        _chunkProcessingHelperFactory = chunkProcessingHelperFactory;
    }

    public string Topic => _configuration[$"Kafka:Nodes:{_nodeId}:ChunkProcessingTopic"] ?? string.Empty;
    public string GroupId => $"chunk-consumer-group-{_nodeId}";

    public async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        var chunkProcessingHelper = _chunkProcessingHelperFactory.Create(_nodeId, _tempFolder, _mainFolder);
        //await chunkProcessingHelper.ProcessingChunk(message, cancellationToken);
        var chunkList = new List<ChunkMessage> { JsonSerializer.Deserialize<ChunkMessage>(message)! };
        await chunkProcessingHelper.ProcessChunksConcurrently(chunkList, cancellationToken);
        //var chunk = JsonSerializer.Deserialize<ChunkMessage>(message);
        //if (chunk == null || string.IsNullOrWhiteSpace(chunk.ChunkId))
        //{
        //    _logger.LogWarning("⚠️ Invalid chunk message received: {Message}", message);
        //    return;
        //}

        //_logger.LogInformation("📦 Node {nodeId} received chunk {ChunkId} for file {FileId}", _nodeId, chunk.ChunkId, chunk.FileId);

        //try
        //{
        //    var policy = Policy.Handle<Exception>().RetryAsync(3, onRetry: (ex, count) =>
        //        _logger.LogWarning(ex, "Retry {RetryCount} for chunk {ChunkId}", count, chunk.ChunkId));

        //    await policy.ExecuteAsync(async () =>
        //    {
        //        using var client = _httpClientFactory.CreateClient();
        //        using var response = await client.GetAsync(chunk.BlobUrl, cancellationToken);
        //        response.EnsureSuccessStatusCode();

        //        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        //        _logger.LogInformation("📥 Downloaded {Bytes} bytes for chunk {ChunkId}", data.Length, chunk.ChunkId);

        //        // ✅ Optional: Validate checksum or size
        //        if (chunk.Size != 0 && data.Length != chunk.Size)
        //        {
        //            _logger.LogWarning("❗ Chunk {ChunkId} size mismatch. Expected {Expected}, got {Actual}", chunk.ChunkId, chunk.Size, data.Length);
        //            return;
        //        }

        //        // ⬇️ Store / Process chunk data here (e.g., save to disk, memory, DB, stream...)
        //        var chunkFolder = Path.Combine(_tempFolder, chunk.FileId);
        //        Directory.CreateDirectory(chunkFolder);
        //        var chunkPath = Path.Combine(chunkFolder, chunk.ChunkId);
        //        await File.WriteAllBytesAsync(chunkPath, data, cancellationToken);

        //        if (chunk.IsLast)
        //        {
        //            var total = chunk.TotalChunks;
        //            var current = await _redisService.GetProcessedChunks(chunk.FileId, _nodeId);

        //            if (current == total)
        //            {
        //                _logger.LogInformation($"🎉 Node {_nodeId} completed all chunks for file {chunk.FileId}");
        //                // ➕ Optional: trigger merge or notify via Kafka
        //                await MergeChunks(chunk.FullFileName, chunk.FileId, current);
        //            }
        //        }
        //    });
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogError(ex, "❌ Error handling chunk {ChunkId}");
        //}
    }

    //public async Task MergeChunks(string fileName, string fileId, int totalChunks)
    //{
    //    if (!Directory.Exists(_mainFolder))
    //    {
    //        Directory.CreateDirectory(_mainFolder);
    //    }

    //    var inputDir = Path.Combine(_tempFolder, fileId);
    //    var outputPath = Path.Combine(_mainFolder, fileName);

    //    using var output = new FileStream(outputPath, FileMode.Create);
    //    for (int i = 0; i < totalChunks; i++)
    //    {
    //        var chunkPath = Path.Combine(inputDir, $"chunk_{i}");
    //        if (!File.Exists(chunkPath)) throw new FileNotFoundException($"Missing chunk: {chunkPath}");

    //        var data = await File.ReadAllBytesAsync(chunkPath);
    //        await output.WriteAsync(data);
    //    }

    //    Directory.Delete(_tempFolder, true);

    //    _logger.LogInformation("📦 Merged file saved to {Output}", outputPath);
    //}
}
