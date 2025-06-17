using Confluent.Kafka;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Infrastructure.Services.Kafka
{
    public class KafkaConsumerCoordinator : BackgroundService
    {
        private readonly IEnumerable<IKafkaMessageHandler> _handlers;
        private readonly IConfiguration _config;
        private readonly ILogger<KafkaConsumerCoordinator> _logger;
        private readonly List<Task> _runningConsumers = new();

        public KafkaConsumerCoordinator(
            IEnumerable<IKafkaMessageHandler> handlers,
            IConfiguration config,
            ILogger<KafkaConsumerCoordinator> logger)
        {
            _handlers = handlers;
            _config = config;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var handler in _handlers)
            {
                var task = Task.Run(() => StartConsumerLoop(handler, stoppingToken), stoppingToken);
                _runningConsumers.Add(task);
            }

            return Task.CompletedTask;
        }

        private async Task StartConsumerLoop(IKafkaMessageHandler handler, CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _config["Kafka:BootstrapServers"],
                GroupId = handler.GroupId,
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            using var consumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                .Build();

            consumer.Subscribe(handler.Topic);
            _logger.LogInformation("Subscribed to topic {Topic}", handler.Topic);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(result?.Message?.Value))
                    {
                        await handler.HandleAsync(result.Message.Value, cancellationToken);
                        consumer.Commit(result);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume exception: {Reason}", ex.Error.Reason);
                    await Task.Delay(1000, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception while consuming Kafka topic");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }
    }

}
