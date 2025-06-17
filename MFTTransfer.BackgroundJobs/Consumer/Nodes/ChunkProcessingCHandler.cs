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

public class ChunkProcessingCHandler : IKafkaMessageHandler
{
    private readonly BlobStorageHelper _blobStorageHelper;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRedisService _redisService;
    private readonly IKafkaService _kafkaService;
    private readonly ILogger<ChunkProcessingCHandler> _logger;
    private readonly IChunkProcessingHelperFactory _chunkProcessingHelperFactory;
    private readonly IConfiguration _configuration;
    private readonly string _nodeId;
    private readonly string _tempFolder;
    private readonly string _mainFolder;
    public ChunkProcessingCHandler(
        BlobStorageHelper blobStorageHelper,
        IHttpClientFactory httpClientFactory,
        IRedisService redisService,
        IKafkaService kafkaService,
        IConfiguration configuration,
        IChunkProcessingHelperFactory chunkProcessingHelperFactory,
        ILogger<ChunkProcessingCHandler> logger)
    {
        _blobStorageHelper = blobStorageHelper;
        _httpClientFactory = httpClientFactory;
        _redisService = redisService;
        _kafkaService = kafkaService;
        _logger = logger;
        _configuration = configuration;
        _nodeId = _configuration["NodeCSettings:NodeId"] ?? string.Empty;
        _tempFolder = _configuration["NodeCSettings:TempFolder"] ?? string.Empty;
        _mainFolder = _configuration["NodeCSettings:MainFolder"] ?? string.Empty;
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
    }
}
