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
                var task = Task.Run(() => StartConsumerLoopAsync(handler, stoppingToken), stoppingToken);
                _runningConsumers.Add(task);
            }

            return Task.CompletedTask;
        }

        private async Task StartConsumerLoopAsync(IKafkaMessageHandler handler, CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = _config["Kafka:BootstrapServers"],
                GroupId = handler.GroupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                MaxPollIntervalMs = int.Parse(_config["Kafka:MaxPollIntervalMs"] ?? "300000"),
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<string, string>(config)
                .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
                .Build();

            consumer.Subscribe(handler.Topic);
            _logger.LogInformation("✅ Subscribed to topic {Topic}", handler.Topic);

            var processingSemaphore = new SemaphoreSlim(int.Parse(_config["MaxDegreeOfParallelism"] ?? "4")); // ⬅️ limit parallelism
            var processingTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(cancellationToken);

                    if (!string.IsNullOrWhiteSpace(result?.Message?.Value))
                    {
                        await processingSemaphore.WaitAsync(cancellationToken);

                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await handler.HandleAsync(result.Message.Value, cancellationToken);
                                consumer.Commit(result);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "❌ Error handling Kafka message");
                                // Optionally: NACK or move to dead-letter
                            }
                            finally
                            {
                                processingSemaphore.Release();
                            }
                        }, cancellationToken);

                        processingTasks.Add(task);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "⚠️ Kafka consume exception: {Reason}", ex.Error.Reason);
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🔁 Kafka consumer cancellation requested");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Unhandled exception in Kafka consumer");
                    await Task.Delay(1000, cancellationToken);
                }
            }

            _logger.LogInformation("🛑 Kafka consumer shutting down. Waiting for pending tasks...");

            await Task.WhenAll(processingTasks);
        }
    }

}
