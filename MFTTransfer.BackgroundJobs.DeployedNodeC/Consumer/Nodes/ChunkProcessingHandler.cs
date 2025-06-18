using System.Text;
using System.Text.Json;
using MFTTransfer.BackgroundJobs.Consumer.Nodes;
using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Helpers;
using MFTTransfer.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

public class ChunkProcessingHandler : IKafkaMessageHandler
{
    private readonly ILogger<ChunkProcessingHandler> _logger;
    private readonly IChunkProcessingHelperFactory _chunkProcessingHelperFactory;
    private readonly IConfiguration _configuration;
    private readonly string _nodeId;
    private readonly string _tempFolder;
    private readonly string _mainFolder;

    public ChunkProcessingHandler(
        IConfiguration configuration,
        IChunkProcessingHelperFactory chunkProcessingHelperFactory,
        ILogger<ChunkProcessingHandler> logger)
    {
        _logger = logger;
        _configuration = configuration;
        _nodeId = _configuration["NodeSettings:NodeId"] ?? string.Empty;
        _tempFolder = _configuration["NodeSettings:TempFolder"] ?? string.Empty;
        _mainFolder = _configuration["NodeSettings:MainFolder"] ?? string.Empty;
        _chunkProcessingHelperFactory = chunkProcessingHelperFactory;
    }

    public string Topic => string.Concat(_configuration["Kafka:Topic:ChunkProcessing"], "_", _configuration["NodeSettings:NodeId"]);
    public string GroupId => string.Concat(_configuration["Kafka:Group:ChunkProcessing"], "_", _configuration["NodeSettings:NodeId"]);

    public async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        var chunkProcessingHelper = _chunkProcessingHelperFactory.Create(_nodeId, _tempFolder, _mainFolder);
        //await chunkProcessingHelper.ProcessingChunk(message, cancellationToken);
        var chunkList = new List<ChunkMessage> { JsonSerializer.Deserialize<ChunkMessage>(message)! };
        await chunkProcessingHelper.ProcessChunksConcurrently(chunkList, cancellationToken);
    }
}
