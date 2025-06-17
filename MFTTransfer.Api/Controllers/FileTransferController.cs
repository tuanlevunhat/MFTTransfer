using MFTTransfer.Api.Models;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure;
using MFTTransfer.Infrastructure.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace MFTTransfer.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [EnableCors("AllowReact")]
    public class FileTransferController : ControllerBase
    {
        private readonly FileTransferService _fileTransferService;
        private readonly ILogger<FileTransferController> _logger;
        private readonly IBlobService _blobService;
        private readonly IKafkaService _kafkaService;
        private readonly IRedisService _redisService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        public FileTransferController(IBlobService blobService,
            IKafkaService kafkaService,
            IRedisService redisService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            FileTransferService fileTransferService,
            ILogger<FileTransferController> logger)
        {
            _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _kafkaService = kafkaService ?? throw new ArgumentNullException(nameof(kafkaService));
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileTransferService = fileTransferService;
        }

        [HttpPost("transfer")]
        public async Task<IActionResult> UploadFile([FromBody] TransferRequest transferRequest)
        {
            if (transferRequest == null || string.IsNullOrWhiteSpace(transferRequest.FullPath))
                return BadRequest("File path is required.");

            if (!System.IO.File.Exists(transferRequest.FullPath))
                return NotFound($"File not found: {transferRequest.FullPath}");

            try
            {
                var fileId = Guid.NewGuid().ToString();
                var fileInfo = new FileInfo(transferRequest.FullPath);

                if (fileInfo.Length == 0)
                    return BadRequest("File is empty.");

                if (fileInfo.Length > 5L * 1024 * 1024 * 1024)
                    return BadRequest("File exceeds maximum allowed size (5GB).");

                _logger.LogInformation("📤 Sending file info to Kafka: {FilePath}, size: {Size} bytes", fileInfo.FullName, fileInfo.Length);

                var initMessage = new FileTransferInitMessage
                {
                    FileId = fileId,
                    FullName = fileInfo.Name,
                    FullPath = transferRequest.FullPath,
                    Size = fileInfo.Length,
                    ReceivedNodes = transferRequest.ReceivedNodes
                };

                await _kafkaService.SendInitTransferMessage(initMessage);

                return Ok(new { FileId = fileId, Size = fileInfo.Length });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initiate file transfer");
                return StatusCode(500, new { Message = ex.Message });
            }
        }

        [HttpPost("transferfile")]
        public async Task<IActionResult> TransferFile([FromBody] TransferRequest transferRequest)
        {
            if (transferRequest == null || string.IsNullOrWhiteSpace(transferRequest.FullPath))
                return BadRequest("File path is required.");

            if (!System.IO.File.Exists(transferRequest.FullPath))
                return NotFound($"File not found: {transferRequest.FullPath}");

            try
            {
                using var fileStream = new FileStream(transferRequest.FullPath, FileMode.Open, FileAccess.Read);
                var fileSize = fileStream.Length;

                if (fileSize == 0)
                    return BadRequest("File is empty.");

                if (fileSize > 5L * 1024 * 1024 * 1024) // 5GB
                    return BadRequest("File exceeds maximum allowed size (5GB).");

                _logger.LogInformation("🔄 Starting upload for file {File} with size {Size} bytes", fileStream.Name, fileSize);

                const int chunkSize = 10 * 1024 * 1024; // 10MB
                var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);
                var fileId = Guid.NewGuid().ToString();

                await _redisService.SetTotalChunks(fileId, totalChunks);

                var semaphore = new SemaphoreSlim(4); // Limit max concurrent uploads
                var tasks = new List<Task>();

                for (int i = 0; i < totalChunks; i++)
                {
                    var chunkIndex = i;
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var chunkId = $"chunk_{chunkIndex}";
                            var buffer = new byte[chunkSize];

                            lock (fileStream) // ensure thread-safe read
                            {
                                fileStream.Position = chunkIndex * chunkSize;
                                fileStream.Read(buffer, 0, chunkSize);
                            }

                            using var ms = new MemoryStream(buffer);
                            var chunkMetadata = await _blobService.UploadBlobPerChunk(ms, fileId, chunkId);
                            await _redisService.SaveChunkMetadata(fileId, chunkId, chunkMetadata);
                            if (chunkMetadata == null)
                            {
                                _logger.LogError("❌ Upload failed for chunk {ChunkId} of file {FileId}", chunkId, fileId);
                                await _redisService.MarkUploadChunkFailed(fileId, chunkId); // optional
                                return;
                            }

                            foreach (var node in transferRequest.ReceivedNodes)
                            {
                                var topic = GetTopicForNode(node);
                                if (string.IsNullOrWhiteSpace(topic))
                                {
                                    _logger.LogWarning("❌ Node {NodeId} has no configured topic", node);
                                    continue;
                                }

                                await _kafkaService.SendChunkMessageToNode(topic, new ChunkMessage
                                {
                                    FileId = fileId,
                                    ChunkId = chunkId,
                                    BlobUrl = chunkMetadata.BlobUrl,
                                    Size = chunkSize,
                                    TotalChunks = totalChunks,
                                    IsLast = (chunkIndex == totalChunks - 1)
                                });

                                _logger.LogInformation("📤 Sent chunk to node {Node} via topic {Topic}", node, topic);
                            }

                            await _redisService.AddBlock(fileId, Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId)));

                            _logger.LogInformation("✅ Uploaded chunk {ChunkId} for file {FileId}", chunkId, fileId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Unexpected error in chunk {ChunkIndex} upload", chunkIndex);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                _logger.LogInformation("✅ Finished initiating upload for file {FileId} with {ChunkCount} chunks", fileId, totalChunks);
                return Ok(new { FileId = fileId, TotalChunks = totalChunks });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ File transfer failed");
                return StatusCode(500, new { Message = ex.Message });
            }
        }

        [HttpPost("retry/{fileId}")]
        public async Task<IActionResult> RetryFailedChunks(string fileId, [FromBody] TransferRetryRequest request)
        {
            if (string.IsNullOrEmpty(fileId) || string.IsNullOrWhiteSpace(request?.FullPath))
                return BadRequest("FileId and FullPath are required.");

            if (!System.IO.File.Exists(request.FullPath))
                return NotFound($"File not found at path: {request.FullPath}");

            try
            {
                var result = await _fileTransferService.RetryFailedChunksAsync(fileId, request.FullPath);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying failed chunks for file {FileId}", fileId);
                return StatusCode(500, new { Message = ex.Message });
            }
        }

        private string? GetTopicForNode(string nodeId)
        {
            return _configuration[$"Kafka:Nodes:{nodeId}:ChunkProcessingTopic"];
        }
    }

}
