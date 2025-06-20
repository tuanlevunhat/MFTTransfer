using MFTTransfer.Domain;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MFTTransfer.BackgroundJobs.Consumer
{
    public class TransferCompleteHandler : IKafkaMessageHandler
    {
        private readonly ILogger<TransferCompleteHandler> _logger;
        private readonly IBlobService _blobService;
        private readonly IRedisService _redisService;
        private readonly IConfiguration _configuration;

        public TransferCompleteHandler(
            ILogger<TransferCompleteHandler> logger,
            IConfiguration configuration,
            IBlobService blobService,
            IRedisService redisService)
        {
            _logger = logger;
            _blobService = blobService;
            _redisService = redisService;
            _configuration = configuration;
        }

        public string GroupId => string.Concat(_configuration["Kafka:Group:TransferComplete"], "_", _configuration["NodeSettings:NodeId"]);
        public string Topic => string.Concat(_configuration["Kafka:Topic:TransferComplete"], "_", _configuration["NodeSettings:NodeId"]);

        public async Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔥 Tranfer Complete received: {Message}", message);
            var transferCompleteMessage = JsonSerializer.Deserialize<TransferProgressMessage>(message);
            if (transferCompleteMessage != null)
            {
                var receivingNodes = await _redisService.GetReceivingNodesAsync(transferCompleteMessage.FileId);
                var completeNodes = await _redisService.GetTransferCompleteNodesAsync(transferCompleteMessage.FileId);
                if (receivingNodes.Count == completeNodes.Count && receivingNodes.Any(x => completeNodes.Contains(x)))
                {
                    await _redisService.CleanUpCacheFileAsync(transferCompleteMessage.FileId);
                    _logger.LogInformation("🧹 Cleaned Redis keys for fileId: {FileId}", transferCompleteMessage.FileId);
                    await _blobService.CleanUpBlobChunks(transferCompleteMessage.FileId);
                    _logger.LogInformation("🧹 Cleaned Temp Blobs for fileId: {FileId}", transferCompleteMessage.FileId);
                }
            }
        }
    }

}
