using System.Text.Json;
using MFTTransfer.Domain;
using MFTTransfer.Monitoring;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace MFTTransfer.BackgroundJobs.Consumer
{
    public class StatusUpdateHandler : IKafkaMessageHandler
    {
        private readonly ILogger<StatusUpdateHandler> _logger;
        private readonly IConfiguration _configuration;

        public StatusUpdateHandler(ILogger<StatusUpdateHandler> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public string Topic => string.Concat(_configuration["Kafka:Topic:StatusUpdate"], "_", _configuration["NodeSettings:NodeId"]);
        public string GroupId => string.Concat(_configuration["Kafka:Group:StatusUpdate"], "_", _configuration["NodeSettings:NodeId"]);

        public async Task HandleAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                var status = JsonSerializer.Deserialize<TransferStatus>(message);
                if (status == null)
                {
                    _logger.LogWarning("⚠️ Cannot deserialize TransferStatus from message: {Message}", message);
                    await Task.CompletedTask;
                    return;
                }

                PrometheusMetrics.SetTransferProgress(status.Progress);
                _logger.LogInformation("📈 Updated progress for file {FileId}: {Status}", status.FileId, status.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing status update for message: {Message}", message);
                throw;
            }
        }
    }
}