using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Infrastructure.Services
{
    public class FileTransferService
    {
        private readonly IRedisService _redisService;
        private readonly IBlobService _blobService;
        private readonly IKafkaService _kafkaService;
        private readonly ILogger<FileTransferService> _logger;

        public FileTransferService(
            IRedisService redisService,
            IBlobService blobService,
            IKafkaService kafkaService,
            ILogger<FileTransferService> logger)
        {
            _redisService = redisService;
            _blobService = blobService;
            _kafkaService = kafkaService;
            _logger = logger;
        }

        public async Task<object> RetryFailedChunksAsync(string fileId, string fullPath)
        {
            var failedChunks = await _redisService.GetUploadFailedChunksAsync(fileId);
            if (failedChunks == null || !failedChunks.Any())
                return new { Message = "No failed chunks to retry." };

            const int chunkSize = 10 * 1024 * 1024;
            var total = failedChunks.Count;
            int success = 0, failed = 0;

            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);

            foreach (var chunkId in failedChunks)
            {
                try
                {
                    if (!int.TryParse(chunkId.Replace("chunk_", ""), out int index))
                        continue;

                    var buffer = new byte[chunkSize];
                    fileStream.Position = index * chunkSize;
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
                    if (bytesRead == 0) continue;

                    using var ms = new MemoryStream(buffer, 0, bytesRead);
                    var metadata = await _blobService.UploadChunk(ms, fileId, chunkId);

                    if (metadata != null)
                    {
                        var totalChunks = await _redisService.GetTotalChunksAsync(fileId);
                        await _kafkaService.SendChunkMessage(fileId, new ChunkMessage()
                        {
                            FileId = fileId,
                            ChunkId = chunkId,
                            BlobUrl = metadata.BlobUrl,
                            Checksum = metadata.Checksum,
                            Size = chunkSize,
                            TotalChunks = totalChunks,
                            IsLast = (index == totalChunks - 1)
                        });
                        await _redisService.RemoveUploadChunkFromFailedAsync(fileId, chunkId);
                        success++;
                    }
                    else failed++;
                }
                catch
                {
                    failed++;
                }
            }

            return new
            {
                FileId = fileId,
                TotalFailed = total,
                Retried = success,
                FailedAgain = failed,
                Message = "Retry process completed."
            };
        }
    }
}
