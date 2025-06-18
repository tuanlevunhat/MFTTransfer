using Confluent.Kafka;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs
{
    public class KafkaCommitWorker
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly IRedisService _redisService;
        private readonly IBlobService _blobService;
        private readonly ILogger<KafkaCommitWorker> _logger;

        public KafkaCommitWorker(
            string kafkaBroker,
            string topic,
            IRedisService redisService,
            IBlobService blobService,
            ILogger<KafkaCommitWorker> logger)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = kafkaBroker,
                GroupId = "commit_group",
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            _consumer = new ConsumerBuilder<string, string>(config).Build();
            _consumer.Subscribe(topic);

            _redisService = redisService;
            _blobService = blobService;
            _logger = logger;
        }

        public void StartListening()
        {
            while (true)
            {
                try
                {
                    var cr = _consumer.Consume();
                    var msg = JsonSerializer.Deserialize<ChunkMessage>(cr.Message.Value);

                    _logger.LogInformation("📥 Received chunk message: {ChunkId} (isLast: {IsLast})", msg.ChunkId, msg.IsLast);

                    if (msg.IsLast)
                    {
                        _ = Task.Run(async () =>
                        {
                            await TryCommitIfComplete(msg.FileId);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error processing Kafka chunk message");
                }
            }
        }

        private async Task TryCommitIfComplete(string fileId)
        {
            var uploaded = await _redisService.CountUploadedBlocks(fileId);
            var total = await _redisService.GetTotalChunks(fileId);

            _logger.LogInformation("🧮 Checking commit condition: {Uploaded}/{Total} for file {FileId}", uploaded, total, fileId);

            if (uploaded == total)
            {
                await _blobService.CommitFileBlocksAsync(fileId);
                _logger.LogInformation("✅ Committed final blob for file {FileId}", fileId);
            }
            else
            {
                _logger.LogWarning("⏳ Not enough chunks uploaded yet: {Uploaded}/{Total} for file {FileId}", uploaded, total, fileId);
            }
        }
    }
}
