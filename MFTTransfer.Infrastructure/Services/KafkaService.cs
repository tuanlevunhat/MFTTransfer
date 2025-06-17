using Confluent.Kafka;
using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Services.Kafka;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Text.Json;
using static Confluent.Kafka.ConfigPropertyNames;

namespace MFTTransfer.Infrastructure.Services
{
    public class KafkaService : IKafkaService
    {
        private readonly BaseProducerService _producerService;
        private readonly ILogger<KafkaService> _logger;

        public KafkaService(BaseProducerService producerService, ILogger<KafkaService> logger)
        {
            _producerService = producerService;
            _logger = logger;
        }

        private async Task SendAsync(string topic, Message<string, string> message)
        {
            try
            {
                _logger.LogInformation("🚀 Sending Kafka message to topic {Topic}: {Key}", topic, message.Key);
                var deliveryResult = await _producerService.Producer.ProduceAsync(topic, message);
                _logger.LogInformation("✅ Kafka message sent to {TopicPartitionOffset}", deliveryResult.TopicPartitionOffset);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
            }
        }

        public Task SendInitTransferMessage(FileTransferInitMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Init transfer message sent for file {FileId}", message.FileId);
                var value = JsonSerializer.Serialize(message);
                return SendAsync(KafkaConstant.KafkaFileTransferInitTopic, new Message<string, string> { Key = message.FileId, Value = value });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public async Task SendChunkMessageToNode(string topic, ChunkMessage chunkMessage)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic must be provided.", nameof(topic));

            if (chunkMessage == null)
                throw new ArgumentNullException(nameof(chunkMessage));

            var json = JsonSerializer.Serialize(chunkMessage);

            var message = new Message<string, string>
            {
                Key = chunkMessage.FileId,
                Value = json
            };

            try
            {
                _logger.LogInformation("📨 Sending chunk {ChunkId} of file {FileId} to topic {Topic}", chunkMessage.ChunkId, chunkMessage.FileId, topic);
                var result = await _producerService.Producer.ProduceAsync(topic, message);
                _logger.LogInformation("✅ Chunk message sent to topic {Topic} at offset {Offset}", topic, result.Offset);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Failed to send chunk message to topic {Topic}", topic);
                throw;
            }
        }

        public Task SendChunkMessage(string fileId, ChunkMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Chunk message sent for file {FileId}", message.FileId);
                var value = JsonSerializer.Serialize(message);
                return SendAsync(KafkaConstant.KafkaChunkProcessingTopic, new Message<string, string> { Key = fileId, Value = value });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public Task SendFinalizeUploadChunksMessage(string fileId)
        {
            try
            {
                _logger.LogInformation("📨 Finalize upload chunks message sent for file {FileId}", fileId);
                return SendAsync(KafkaConstant.KafkaFinalizeUploadChunksTopic, new Message<string, string>
                {
                    Key = fileId,
                    Value = fileId
                });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public Task SendTransferProgressMessage(string fileId, TransferProgressMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Subscribe to status message sent for topic {topic}", KafkaConstant.KafkaTransferProgressTopic);
                return SendAsync(KafkaConstant.KafkaTransferProgressTopic, new Message<string, string>
                {
                    Key = fileId,
                    Value = JsonSerializer.Serialize(message)
                });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public Task SendTransferCompleteMessage(string fileId, TransferProgressMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Subscribe to status message sent for topic {topic}", KafkaConstant.KafkaTransferCompleteTopic);
                return SendAsync(KafkaConstant.KafkaTransferCompleteTopic, new Message<string, string>
                {
                    Key = fileId,
                    Value = JsonSerializer.Serialize(message)
                });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public Task SendRetryChunkRequest(RetryChunkRequest request)
        {
            try
            {
                var topic = KafkaConstant.KafkaRetryDownloadChunkTopic;
                var message = JsonSerializer.Serialize(request);

                _logger.LogInformation("📨 RetryChunkRequest sent for FileId={FileId}, ChunkId={ChunkId}, NodeId={NodeId}",
                    request.FileId, request.ChunkId, request.NodeId);
                return SendAsync(topic, new Message<string, string> { Key = request.FileId, Value = message });

            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }
    }
}