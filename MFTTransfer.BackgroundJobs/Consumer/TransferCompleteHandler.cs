using MFTTransfer.Domain;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs
{
    public class TransferCompleteHandler : IKafkaMessageHandler
    {
        private readonly ILogger<TransferCompleteHandler> _logger;
        private readonly IBlobService _blobService;
        private readonly IRedisService _redisService;

        public TransferCompleteHandler(
            ILogger<TransferCompleteHandler> logger,
            IBlobService blobService,
            IRedisService redisService)
        {
            _logger = logger;
            _blobService = blobService;
            _redisService = redisService;
        }

        public string Topic => KafkaConstant.KafkaTransferCompleteTopic;
        public string GroupId => KafkaConstant.KafkaGroupId;

        public async Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            _logger.LogInformation("🔥 Tranfer Complete received: {Message}", message);
            var transferCompleteMessage = JsonSerializer.Deserialize<TransferProgressMessage>(message);
            if (transferCompleteMessage != null)
            {
                var receivingNodes = await _redisService.GetReceivingNodes(transferCompleteMessage.FileId);
                var completeNodes = await _redisService.GetTransferCompleteNodes(transferCompleteMessage.FileId);
                if (receivingNodes.Count == completeNodes.Count && receivingNodes.Any(x => completeNodes.Contains(x)))
                {
                    await _redisService.CleanUpCacheFile(transferCompleteMessage.FileId);
                    _logger.LogInformation("🧹 Cleaned Redis keys for fileId: {FileId}", transferCompleteMessage.FileId);
                    await _blobService.CleanUpBlobChunks(transferCompleteMessage.FileId);
                    _logger.LogInformation("🧹 Cleaned Temp Blobs for fileId: {FileId}", transferCompleteMessage.FileId);
                }
            }
        }
    }

}
