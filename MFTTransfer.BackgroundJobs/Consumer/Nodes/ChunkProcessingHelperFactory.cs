using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs.Consumer.Nodes
{
    public class ChunkProcessingHelperFactory : IChunkProcessingHelperFactory
    {
        private readonly ILogger<IChunkProcessingHelperFactory> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRedisService _redisService;
        private readonly IKafkaService _kafkaService;

        public ChunkProcessingHelperFactory(
            ILogger<IChunkProcessingHelperFactory> logger,
            IHttpClientFactory httpClientFactory,
            IRedisService redisService,
            IKafkaService kafkaService)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _redisService = redisService;
            _kafkaService = kafkaService;
        }

        public ChunkProcessingHelper Create(string nodeId, string tempFolder, string mainFolder)
        {
            return new ChunkProcessingHelper(_logger, nodeId, tempFolder, mainFolder, _httpClientFactory, _redisService, _kafkaService);
        }
    }

}
