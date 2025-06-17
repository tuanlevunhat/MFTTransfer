using MFTTransfer.Api.Models;
using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure;
using MFTTransfer.Infrastructure.Services;
using MFTTransfer.Utilities;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

[Route("api/[controller]")]
[ApiController]
[EnableCors("AllowReact")]
public class UploadController : ControllerBase
{
    private readonly IBlobService _blobService;
    private readonly IKafkaService _kafkaService;
    private readonly IRedisService _redisService;
    private readonly ILogger<UploadController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    public UploadController(IBlobService blobService,
        IKafkaService kafkaService,
        IRedisService redisService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UploadController> logger)
    {
        _blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
        _kafkaService = kafkaService ?? throw new ArgumentNullException(nameof(kafkaService));
        _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("upload-chunk")]
    public async Task<IActionResult> UploadChunk([FromForm] IFormFile file, [FromForm] string fileId)
    {
        try
        {
            // Zip file (optional)
            //using var compressedStream = CompressionHelper.CompressFile(file.FileName, compressionLevel: 6);
            var chunkMetadatas = ChunkingHelper.SplitIntoChunks(file.FileName, chunkSize: 10 * 1024 * 1024, fileId, _configuration["BlobStorage:ChunksContainer"]);

            foreach (var chunkMetadata in chunkMetadatas)
            {
                using var chunkStream = new MemoryStream(); // compressedStream
                await file.CopyToAsync(chunkStream);
                chunkStream.Position = 0;
                var uploadedMetadata = await _blobService.UploadChunk(chunkStream, fileId, chunkMetadata.ChunkId);
               // await _kafkaService.SendChunkMessage(fileId, uploadedMetadata.Url, chunkMetadata.ChunkId, uploadedMetadata);
            }

            var fileMetadata = new FileMetadata { FileId = fileId, Chunks = chunkMetadatas };

            // Save metadata to Redis (optional)
           // await _cacheManager.SetAsync(fileId), fileMetadata);

            _logger.LogInformation("Uploaded chunks for file {FileId}", fileId);

            return Ok(new { FileId = fileId, ChunkCount = chunkMetadatas.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chunks for file {FileId}", fileId);
            return StatusCode(500, new { Message = ex.Message });
        }
    }

    [HttpPost("initiate")]
    public async Task<IActionResult> InitiateUpload([FromForm] IFormFile file, [FromForm] string fileId)
    {
        try
        {
            if (file == null || string.IsNullOrEmpty(fileId))
            {
                _logger.LogWarning("Invalid input: file={File} or fileId={FileId}", file?.FileName, fileId);
                return BadRequest("File or fileId is missing");
            }

            using var fileStream = file.OpenReadStream();
            _logger.LogInformation("Starting upload for file {FileName} with size {Size} bytes", file.FileName, file.Length);
            var chunkSize = 10 * 1024 * 1024; // 10MB per chunk
            var totalChunks = (int)Math.Ceiling((double)fileStream.Length / chunkSize);
            await _redisService.SetTotalChunks(fileId, totalChunks);

            for (int i = 0; i < totalChunks; i++)
            {
                var chunk = new byte[chunkSize];
                var bytesRead = fileStream.Read(chunk, 0, chunkSize);
                if (bytesRead == 0)
                {
                    _logger.LogWarning("No bytes read for chunk {ChunkId} of file {FileId}", $"chunk_{i}", fileId);
                    continue;
                }

                var chunkId = $"chunk_{i}";
                using var memoryStream = new MemoryStream(chunk, 0, bytesRead);
                _logger.LogDebug("Processing chunk {ChunkId} with {BytesRead} bytes", chunkId, bytesRead);

                var chunkMetadata = await _blobService.UploadChunk(memoryStream, fileId, chunkId);
                if (chunkMetadata == null)
                {
                    _logger.LogError("Failed to upload chunk {ChunkId} for file {FileId}", chunkId, fileId);
                    return StatusCode(500, "Chunk upload failed");
                }

                //await _kafkaService.SendChunkMessage(fileId, chunkMetadata.Url, chunkId, chunkMetadata);
                await _redisService.AddBlock(fileId, Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId)));
                _logger.LogInformation("Successfully uploaded chunk {ChunkId} for file {FileId}", chunkId, fileId);
            }

            _logger.LogInformation("Initiated upload for file {FileId} with {TotalChunks} chunks", fileId, totalChunks);
            return Ok(new { FileId = fileId, TotalChunks = totalChunks });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating upload for {FileId}", fileId);
            return StatusCode(500, new { Message = ex.Message });
        }
    }
}