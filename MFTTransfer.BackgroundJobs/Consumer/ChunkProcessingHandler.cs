using System.Text.Json;
using MFTTransfer.BackgroundJobs.Helpers;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MFTTransfer.BackgroundJobs.Consumer
{
    public class ChunkProcessingHandler : IKafkaMessageHandler
    {
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
            var chunkList = JsonSerializer.Deserialize<List<ChunkMessage>>(message) ?? new List<ChunkMessage>();
            await chunkProcessingHelper.ProcessChunksConcurrentlyAsync(chunkList, cancellationToken);
        }
    }
}