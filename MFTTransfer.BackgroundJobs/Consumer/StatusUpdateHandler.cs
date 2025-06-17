using System.Text.Json;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Domain;
using MFTTransfer.Monitoring;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Logging;

public class StatusUpdateHandler : IKafkaMessageHandler
{
    private readonly ILogger<StatusUpdateHandler> _logger;

    public StatusUpdateHandler(ILogger<StatusUpdateHandler> logger)
    {
        _logger = logger;
    }

    public string Topic => KafkaConstant.KafkaStatusUpdateTopic;
    public string GroupId => KafkaConstant.KafkaGroupId;

    public async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var status = JsonSerializer.Deserialize<TransferStatus>(message);
            if (status == null)
            {
                _logger.LogWarning("⚠️ Cannot deserialize TransferStatus from message: {Message}", message);
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
