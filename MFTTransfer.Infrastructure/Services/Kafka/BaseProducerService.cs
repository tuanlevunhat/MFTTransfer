using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace MFTTransfer.Infrastructure.Services.Kafka
{
    public class BaseProducerService : IDisposable
    {
        public IProducer<string, string> Producer { get; }
        protected readonly ILogger<BaseProducerService> Logger;

        public BaseProducerService(IConfiguration configuration, ILogger<BaseProducerService> logger)
        {
            var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? throw new ArgumentNullException("Kafka:BootstrapServers");
            Logger = logger;

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 1000,
                EnableIdempotence = true
            };

            Producer = new ProducerBuilder<string, string>(producerConfig)
                .SetErrorHandler((_, e) => Logger.LogError("Kafka producer error: {Reason}", e.Reason))
                .SetLogHandler((_, m) => Logger.LogInformation("Kafka producer log: {Message}", m.Message))
                .Build();
        }

        public virtual void Dispose()
        {
            Producer?.Dispose();
        }
    }

}
