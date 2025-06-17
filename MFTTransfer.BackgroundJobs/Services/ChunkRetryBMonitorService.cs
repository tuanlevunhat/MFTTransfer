using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.BackgroundJobs.Consumer.Nodes;

namespace MFTTransfer.BackgroundJobs.Services
{
    public class ChunkRetryBMonitorService : BackgroundService
    {
        private readonly IChunkProcessingHelperFactory _chunkProcessingHelperFactory;
        private readonly ILogger<ChunkRetryBMonitorService> _logger;
        private readonly IRedisService _redisService;
        private readonly string _nodeId;
        private readonly string _tempFolder;
        private readonly string _mainFolder;
        private readonly IConfiguration _configuration;
        public ChunkRetryBMonitorService(
            ILogger<ChunkRetryBMonitorService> logger,
            IRedisService redisService,
            IChunkProcessingHelperFactory chunkProcessingHelperFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _redisService = redisService;
            _configuration = configuration;
            _nodeId = _configuration["NodeBSettings:NodeId"] ?? string.Empty;
            _tempFolder = _configuration["NodeBSettings:TempFolder"] ?? string.Empty;
            _mainFolder = _configuration["NodeBSettings:MainFolder"] ?? string.Empty;
            _chunkProcessingHelperFactory = chunkProcessingHelperFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("📡 ChunkRetryMonitorService started for Node {NodeId}", _nodeId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var activeFiles = await _redisService.GetFilesWithResumableChunks(_nodeId);

                    foreach (var fileId in activeFiles)
                    {
                        _logger.LogInformation("🔁 Checking resumable chunks for file {FileId}...", fileId);
                        var chunkProcessingHelper = _chunkProcessingHelperFactory.Create(_nodeId, _tempFolder, _mainFolder);
                        await chunkProcessingHelper.RequestRetryForResumableChunks(fileId, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error during resumable chunk retry check");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
