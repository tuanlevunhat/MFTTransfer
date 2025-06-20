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
        private readonly ILogger<FileTransferController> _logger;
        private readonly IKafkaService _kafkaService;
        private readonly IConfiguration _configuration;
        public FileTransferController(IBlobService blobService,
            IKafkaService kafkaService,
            IRedisService redisService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            FileTransferService fileTransferService,
            ILogger<FileTransferController> logger)
        {
            _kafkaService = kafkaService ?? throw new ArgumentNullException(nameof(kafkaService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

                if (fileInfo.Length > 10L * 1024 * 1024 * 1024)
                    return BadRequest("File exceeds maximum allowed size (10GB).");

                _logger.LogInformation("📤 Sending file info to Kafka: {FilePath}, size: {Size} bytes", fileInfo.FullName, fileInfo.Length);

                var initMessage = new FileTransferInitMessage
                {
                    TransferNode = transferRequest.TransferNode,
                    FileId = fileId,
                    FullName = fileInfo.Name,
                    FullPath = transferRequest.FullPath,
                    Size = fileInfo.Length,
                    ReceivedNodes = transferRequest.ReceivedNodes
                };

                var topic = string.Concat(_configuration["Kafka:Topic:FileTransferInit"], "_", transferRequest.TransferNode);
                await _kafkaService.SendInitTransferMessageAsync(topic, initMessage);

                return Ok(new { FileId = fileId, Size = fileInfo.Length });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initiate file transfer");
                return StatusCode(500, new { Message = ex.Message });
            }
        }
    }
}
