using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MFTTransfer.Infrastructure.Services.Kafka
{
    public class KafkaTopicManager
    {
        private readonly string _bootstrapServers;
        private readonly ILogger<KafkaTopicManager> _logger;

        public KafkaTopicManager(IConfiguration config, ILogger<KafkaTopicManager> logger)
        {
            _bootstrapServers = config["Kafka:BootstrapServers"]!;
            _logger = logger;
        }

        public async Task EnsureTopicExistsAsync(string topicName, int numPartitions = 1, short replicationFactor = 1)
        {
            using var adminClient = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _bootstrapServers
            }).Build();

            try
            {
                var metadata = adminClient.GetMetadata(topicName, TimeSpan.FromSeconds(5));
                if (metadata.Topics.Any(t => t.Topic == topicName && !t.Error.IsError))
                {
                    _logger.LogInformation("✔️ Kafka topic '{Topic}' already exists", topicName);
                    return;
                }
            }
            catch (KafkaException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                _logger.LogWarning("⚠️ Kafka topic '{Topic}' does not exist. Creating...", topicName);
            }

            try
            {
                await adminClient.CreateTopicsAsync(new[]
                {
                new TopicSpecification
                {
                    Name = topicName,
                    NumPartitions = numPartitions,
                    ReplicationFactor = replicationFactor
                }
            });

                _logger.LogInformation("✅ Created Kafka topic '{Topic}'", topicName);
            }
            catch (CreateTopicsException ex) when (ex.Results.Any(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                _logger.LogWarning("⚠️ Topic '{Topic}' was created by another node", topicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create Kafka topic '{Topic}'", topicName);
                throw;
            }
        }
    }

}
