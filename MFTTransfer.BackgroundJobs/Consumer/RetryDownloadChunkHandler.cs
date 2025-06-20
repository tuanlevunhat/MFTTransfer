using System.Text.Json;
using MFTTransfer.BackgroundJobs.Helpers;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MFTTransfer.BackgroundJobs.Consumer
{
    public class RetryDownloadChunkHandler : IKafkaMessageHandler
    {
        private readonly ILogger<RetryDownloadChunkHandler> _logger;
        private readonly IChunkProcessingHelperFactory _chunkProcessingHelperFactory;
        private readonly IConfiguration _configuration;

        public RetryDownloadChunkHandler(
            ILogger<RetryDownloadChunkHandler> logger,
            IChunkProcessingHelperFactory chunkProcessingHelperFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _chunkProcessingHelperFactory = chunkProcessingHelperFactory;
            _configuration = configuration;
        }

        public string Topic => string.Concat(_configuration["Kafka:Topic:RetryDownloadChunk"], "_", _configuration["NodeSettings:NodeId"]);
        public string GroupId => string.Concat(_configuration["Kafka:Group:RetryDownloadChunk"], "_", _configuration["NodeSettings:NodeId"]);

        public async Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                var request = JsonSerializer.Deserialize<RetryChunkRequest>(message);
                if (request == null)
                {
                    _logger.LogWarning("⚠️ Invalid RetryChunkRequest: {Message}", message);
                    return;
                }

                _logger.LogInformation("🔄 Received retry request for Chunk {ChunkId} of File {FileId}", request.ChunkId, request.FileId);

                var _chunkProcessingHelper = _chunkProcessingHelperFactory.Create(string.Empty, string.Empty, string.Empty);
                await _chunkProcessingHelper.HandleRetryChunkAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling retry-download-chunk message");
            }
        }
    }
}
