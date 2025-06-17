using MFTTransfer.Infrastructure.Constants;
using MFTTransfer.Infrastructure.Services.Kafka;
using Microsoft.Extensions.Configuration;

namespace MFTTransfer.Infrastructure
{
    public class AppInitializer
    {
        private readonly KafkaTopicManager _topicManager;
        private readonly IConfiguration _configuration;

        public AppInitializer(KafkaTopicManager topicManager,
            IConfiguration configuration)
        {
            _topicManager = topicManager;
            _configuration = configuration;
        }

        public async Task InitKafkaTopicsAsync()
        {
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaChunkProcessingTopic);
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaFinalizeUploadChunksTopic);
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaTransferCompleteTopic);
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaFileTransferInitTopic);
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaRetryDownloadChunkTopic);
            await _topicManager.EnsureTopicExistsAsync(KafkaConstant.KafkaStatusUpdateTopic);
            await _topicManager.EnsureTopicExistsAsync(_configuration["Kafka:Nodes:NFT_B:ChunkProcessingTopic"]);
            await _topicManager.EnsureTopicExistsAsync(_configuration["Kafka:Nodes:NFT_C:ChunkProcessingTopic"]);
            await _topicManager.EnsureTopicExistsAsync(_configuration["Kafka:Nodes:NFT_D:ChunkProcessingTopic"]);
        }
    }
}
