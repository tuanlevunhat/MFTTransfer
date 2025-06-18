using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Services.Kafka;
using Microsoft.Extensions.Configuration;

namespace MFTTransfer.Infrastructure
{
    public class AppInitializer
    {
        private readonly KafkaTopicManager _topicManager;
        private readonly IConfiguration _configuration;
        private readonly string _fileTransferInitTopic;
        private readonly string _chunkProcessingTopic;
        private readonly string _transferProgressTopic;
        private readonly string _transferCompleteTopic;
        private readonly string _retryDownloadChunkTopic;
        private readonly string _statusUpdateTopic;

        public AppInitializer(KafkaTopicManager topicManager,
            IConfiguration configuration)
        {
            _topicManager = topicManager;
            _configuration = configuration;
            _fileTransferInitTopic = string.Concat(_configuration["Kafka:Topic:FileTransferInit"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
            _chunkProcessingTopic = string.Concat(_configuration["Kafka:Topic:ChunkProcessing"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
            _transferProgressTopic = string.Concat(_configuration["Kafka:Topic:TransferProgress"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
            _transferCompleteTopic = string.Concat(_configuration["Kafka:Topic:TransferComplete"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
            _retryDownloadChunkTopic = string.Concat(_configuration["Kafka:Topic:RetryDownloadChunk"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
            _statusUpdateTopic = string.Concat(_configuration["Kafka:Topic:StatusUpdate"] ?? string.Empty, "_", _configuration["NodeSettings:NodeId"] ?? string.Empty);
        }

        public async Task InitKafkaTopicsAsync()
        {
            await _topicManager.EnsureTopicExistsAsync(_fileTransferInitTopic);
            await _topicManager.EnsureTopicExistsAsync(_chunkProcessingTopic);
            await _topicManager.EnsureTopicExistsAsync(_transferProgressTopic);
            await _topicManager.EnsureTopicExistsAsync(_transferCompleteTopic);
            await _topicManager.EnsureTopicExistsAsync(_retryDownloadChunkTopic);
            await _topicManager.EnsureTopicExistsAsync(_statusUpdateTopic);
        }
    }
}
