using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs.Helpers
{
    public class ChunkProcessingHelperFactory : IChunkProcessingHelperFactory
    {
        private readonly ILogger<IChunkProcessingHelperFactory> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRedisService _redisService;
        private readonly IKafkaService _kafkaService;
        private readonly IConfiguration _configuration;

        public ChunkProcessingHelperFactory(
            ILogger<IChunkProcessingHelperFactory> logger,
            IHttpClientFactory httpClientFactory,
            IRedisService redisService,
            IKafkaService kafkaService,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _redisService = redisService;
            _kafkaService = kafkaService;
            _configuration = configuration;
        }

        public ChunkProcessingHelper Create(string nodeId, string tempFolder, string mainFolder)
        {
            return new ChunkProcessingHelper(_logger, nodeId, tempFolder, mainFolder, _httpClientFactory, _redisService, _kafkaService, _configuration);
        }
    }

}
