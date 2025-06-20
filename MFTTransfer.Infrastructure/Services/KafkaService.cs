using Confluent.Kafka;
using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Services.Kafka;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _configuration;
        private readonly string _fileTransferInitTopic;
        private readonly string _kafkaChunkProcessingTopic;
        private readonly string _kafkaTransferProgressTopic;
        private readonly string _kafkaTransferCompleteTopic;
        private readonly string _kafkaRetryDownloadChunkTopic;
        private readonly string _kafkaStatusUpdateTopic;

        public KafkaService(BaseProducerService producerService, ILogger<KafkaService> logger, IConfiguration configuration)
        {
            _producerService = producerService;
            _logger = logger;
            _configuration = configuration;
            _fileTransferInitTopic = _configuration["Kafka:Topic:FileTransferInit"] ?? string.Empty;
            _kafkaChunkProcessingTopic = _configuration["Kafka:Topic:ChunkProcessing"] ?? string.Empty;
            _kafkaTransferProgressTopic = _configuration["Kafka:Topic:TransferProgress"] ?? string.Empty;
            _kafkaTransferCompleteTopic = _configuration["Kafka:Topic:TransferComplete"] ?? string.Empty;
            _kafkaRetryDownloadChunkTopic = _configuration["Kafka:Topic:RetryDownloadChunk"] ?? string.Empty;
            _kafkaStatusUpdateTopic = _configuration["Kafka:Topic:StatusUpdate"] ?? string.Empty;
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

        public Task SendInitTransferMessageAsync(string topic, FileTransferInitMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Init transfer message sent for file {FileId}", message.FileId);
                var value = JsonSerializer.Serialize(message);
                return SendAsync(topic, new Message<string, string> { Key = message.FileId, Value = value });
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Kafka produce failed: {Reason}", ex.Error.Reason);
                return Task.FromException(ex);
            }
        }

        public async Task SendChunkMessageAsync(string topic, ChunkMessage chunkMessage)
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

        public async Task SendChunkBatchMessageAsync(string topic, string fileId, List<ChunkMessage> chunkMessages)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic must be provided.", nameof(topic));

            if (chunkMessages == null)
                throw new ArgumentNullException(nameof(chunkMessages));

            var json = JsonSerializer.Serialize(chunkMessages);

            var message = new Message<string, string>
            {
                Key = fileId,
                Value = json
            };

            try
            {
                _logger.LogInformation("📨 Sending batch chunks to topic {Topic}", topic);
                var result = await _producerService.Producer.ProduceAsync(topic, message);
                _logger.LogInformation("✅ Batch chunks message sent to topic {Topic} at offset {Offset}", topic, result.Offset);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "❌ Failed to send Batch chunks message to topic {Topic}", topic);
                throw;
            }
        }

        public Task SendChunkMessage(string fileId, ChunkMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Chunk message sent for file {FileId}", message.FileId);
                var value = JsonSerializer.Serialize(message);
                return SendAsync(_kafkaChunkProcessingTopic, new Message<string, string> { Key = fileId, Value = value });
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

        public Task SendTransferProgressMessageAsync(string fileId, TransferProgressMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Subscribe to status message sent for topic {topic}", _kafkaTransferProgressTopic);
                return SendAsync(_kafkaTransferProgressTopic, new Message<string, string>
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

        public Task SendTransferCompleteMessageAsync(string topic, string fileId, TransferProgressMessage message)
        {
            try
            {
                _logger.LogInformation("📨 Subscribe to status message sent for topic {topic}", topic);
                return SendAsync(topic, new Message<string, string>
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

        public Task SendRetryChunkMessageAsync(RetryChunkRequest request)
        {
            try
            {
                var topic = _kafkaRetryDownloadChunkTopic;
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