using Microsoft.Extensions.Logging;
using Azure.Storage.Sas;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Helpers;
using Microsoft.AspNetCore.Http;
using System.Text;
using MFTTransfer.Monitoring;
using Azure.Storage.Blobs.Specialized;
using MFTTransfer.Utilities;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs;

namespace MFTTransfer.Infrastructure.Services
{
    public class BlobService : IBlobService
    {
        private readonly BlobStorageHelper _blobStorageHelper;
        private readonly ILogger<BlobService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IRedisService _redisService;

        public BlobService(BlobStorageHelper blobStorageHelper,
            IConfiguration configuration,
            IRedisService redisService,
            ILogger<BlobService> logger)
        {
            _blobStorageHelper = blobStorageHelper ?? throw new ArgumentNullException(nameof(blobStorageHelper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _blobStorageHelper.EnsureContainersExistAsync().GetAwaiter().GetResult();
            _redisService = redisService ?? throw new ArgumentNullException(nameof(redisService));
        }

        public async Task<ChunkMetadata> UploadBlobPerChunkAsync(Stream stream, string fileId, string chunkId)
        {
            try
            {
                if (stream == null || !stream.CanRead)
                {
                    _logger.LogWarning("❌ Stream is null or unreadable for chunk {ChunkId}", chunkId);
                    return null;
                }

                if (stream.Length == 0)
                {
                    _logger.LogWarning("❌ Stream is empty for chunk {ChunkId}", chunkId);
                    return null;
                }

                stream = EnsureSeekable(stream);

                var blobName = $"{fileId}/{chunkId}";
                var blobClient = _blobStorageHelper.ChunksContainer.GetBlobClient(blobName);

                _logger.LogDebug("📦 Uploading chunk {ChunkId} to blob {BlobName} (size: {Size} bytes)", chunkId, blobName, stream.Length);

                // Ensure position is reset
                stream.Position = 0;
                await blobClient.UploadAsync(stream, overwrite: true);

                _logger.LogInformation("✅ Uploaded blob {BlobName} for chunk {ChunkId}", blobName, chunkId);

                // 🔐 Generate SAS token
                var sasBuilder = new BlobSasBuilder(BlobContainerSasPermissions.Read, DateTime.UtcNow.AddHours(1))
                {
                    BlobContainerName = _blobStorageHelper.ChunksContainer.Name,
                    BlobName = blobName,
                    Resource = "b"
                };

                var sasUrl = blobClient.GenerateSasUri(sasBuilder).ToString();

                stream.Position = 0;
                var checksum = ChunkingHelper.ComputeChecksum(stream);

                var chunkMetadata = new ChunkMetadata
                {
                    ChunkId = chunkId,
                    BlobUrl = sasUrl,
                    Size = stream.Length,
                    Checksum = checksum
                };

                PrometheusMetrics.IncrementChunksProcessed();
                _logger.LogInformation("✅ Prepared metadata for chunk {ChunkId} (Size: {Size}, Checksum: {Checksum})", chunkId, stream.Length, checksum);

                return chunkMetadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading chunk {ChunkId} for file {FileId}", chunkId, fileId);
                throw;
            }
        }

        public async Task<ChunkMetadata> UploadChunk(Stream stream, string fileId, string chunkId)
        {
            try
            {
                if (stream == null || !stream.CanRead)
                {
                    _logger.LogWarning("❌ Stream is null or unreadable for chunk {ChunkId}", chunkId);
                    return null;
                }

                if (stream.Length == 0)
                {
                    _logger.LogWarning("❌ Stream is empty for chunk {ChunkId}", chunkId);
                    return null;
                }

                // ✅ Ensure stream is reset and seekable
                stream = EnsureSeekable(stream);

                var blobName = $"{fileId}";
                var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(chunkId));
                var blobClient = _blobStorageHelper.ChunksContainer.GetBlockBlobClient(blobName);

                if (!stream.CanRead || (stream.CanSeek && stream.Length == 0))
                {
                    _logger.LogError("❌ Stream invalid before StageBlock: CanRead={0}, Length={1}", stream.CanRead, stream.Length);
                    throw new InvalidOperationException("Invalid stream for staging block.");
                }

                _logger.LogDebug("📦 Staging chunk {ChunkId} to blob {BlobName}", chunkId, blobName);
                await _blobStorageHelper.StageBlockAsync(_blobStorageHelper.ChunksContainer, blobName, blockId, stream);
                _logger.LogInformation("✅ Staged block {BlockId} for chunk {ChunkId} ({Size} bytes)", blockId, chunkId, stream.Length);

                // 🔐 Generate SAS URL
                var sasBuilder = new BlobSasBuilder(BlobContainerSasPermissions.Read, DateTime.UtcNow.AddHours(1))
                {
                    BlobContainerName = _blobStorageHelper.ChunksContainer.Name,
                    BlobName = blobName,
                    Resource = "b"
                };

                var sasUrl = blobClient.GenerateSasUri(sasBuilder).ToString();

                // ✅ Reset before checksum
                stream.Position = 0;
                var checksum = ChunkingHelper.ComputeChecksum(stream);

                var chunkMetadata = new ChunkMetadata
                {
                    ChunkId = chunkId,
                    BlobUrl = sasUrl,
                    Size = stream.Length,
                    Checksum = checksum
                };

                PrometheusMetrics.IncrementChunksProcessed();
                _logger.LogInformation("✅ Uploaded chunk {ChunkId} for file {FileId}", chunkId, fileId);

                return chunkMetadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading chunk {ChunkId} for file {FileId}", chunkId, fileId);
                throw;
            }
        }

        public async Task CommitFileBlocksAsync(string fileId)
        {
            var blobName = $"{fileId}";
            var container = _blobStorageHelper.ChunksContainer;
            var blockIds = await _redisService.GetUploadedBlockIdsAsync(fileId);
            if (blockIds == null || !blockIds.Any())
            {
                _logger.LogWarning("⚠️ No blocks found to commit for file {FileId}", fileId);
                return;
            }

            _logger.LogInformation("🧱 Committing {Count} blocks for file {FileId} into blob {BlobName}", blockIds.Count, fileId, blobName);

            var blobClient = container.GetBlockBlobClient(blobName);
            await blobClient.CommitBlockListAsync(blockIds.ToList());

            _logger.LogInformation("✅ Committed blob {BlobName} with {BlockCount} blocks", blobName, blockIds.Count);
        }

        public async Task<string> FinalizeUpload(string fileId, List<string> blockList)
        {
            try
            {
                if (blockList == null || !blockList.Any())
                    throw new ArgumentException("Block list is empty or null", nameof(blockList));

                var blobName = $"{_configuration["BlobStorage:CompletedContainer"]}/{fileId}";
                _logger.LogDebug("Committing block list for fileId {FileId} to blobName {BlobName}: {BlockList}", fileId, blobName, string.Join(",", blockList));

                var blobClient = _blobStorageHelper.CompletedContainer.GetBlockBlobClient(blobName);
                // Kiểm tra block đã staged
                var existingBlocks = await blobClient.GetBlockListAsync();
                _logger.LogDebug("Existing staged blocks: {ExistingBlocks}", string.Join(",", existingBlocks.Value.UncommittedBlocks.Select(b => b.Name)));

                await _blobStorageHelper.CommitBlockListAsync(_blobStorageHelper.CompletedContainer, blobName, blockList);

                var fileMetadata = new FileMetadata
                {
                    FileId = fileId,
                    FileName = fileId,
                    Chunks = await GetChunkMetadataListAsync(fileId),
                    TotalSize = (await blobClient.GetPropertiesAsync()).Value.ContentLength,
                    OverallChecksum = await ComputeOverallChecksumAsync(fileId),
                    CreatedAt = DateTime.UtcNow
                };

                var metadataBlobName = $"{_configuration["BlobStorage:MetadataContainer"]}/{fileId}.json";
                await _blobStorageHelper.UploadBlobAsync(
                    _blobStorageHelper.MetadataContainer,
                    metadataBlobName,
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(fileMetadata))));

                PrometheusMetrics.IncrementFilesProcessed();
                PrometheusMetrics.SetTransferProgress(100);
                _logger.LogInformation("Finalized upload for file {FileId}", fileId);
                return blobClient.Uri.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing upload for file {FileId}", fileId);
                throw;
            }
        }

        /// <summary>
        /// Retrieves the list of chunk metadata for a given file from the metadata container.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <returns>A list of ChunkMetadata objects.</returns>
        private async Task<List<ChunkMetadata>> GetChunkMetadataListAsync(string fileId)
        {
            try
            {
                var metadataBlobName = $"{_configuration["BlobStorage:MetadataContainer"]}/{fileId}.json";
                using var stream = await _blobStorageHelper.DownloadBlobAsync(_blobStorageHelper.MetadataContainer, metadataBlobName);
                if (stream == null)
                {
                    _logger.LogWarning("No metadata found for file {FileId}", fileId);
                    return new List<ChunkMetadata>();
                }

                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var fileMetadata = JsonSerializer.Deserialize<FileMetadata>(json);
                return fileMetadata?.Chunks ?? new List<ChunkMetadata>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunk metadata list for file {FileId}", fileId);
                throw;
            }
        }

        /// <summary>
        /// Computes the overall checksum of the finalized file in the completed container.
        /// </summary>
        /// <param name="fileId">The unique identifier of the file.</param>
        /// <returns>The checksum (base64-encoded) of the entire file.</returns>
        private async Task<string> ComputeOverallChecksumAsync(string fileId)
        {
            try
            {
                var blobClient = _blobStorageHelper.CompletedContainer.GetBlockBlobClient(fileId);
                var response = await blobClient.DownloadAsync();
                using var stream = response.Value.Content;

                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(stream);
                return Convert.ToBase64String(hash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error computing overall checksum for file {FileId}", fileId);
                throw;
            }
        }

        public static Stream EnsureSeekable(Stream original)
        {
            if (original.CanSeek && original.Position == 0)
                return original;

            if (original.CanSeek)
            {
                original.Position = 0;
                return original;
            }

            var mem = new MemoryStream();
            original.CopyTo(mem);
            mem.Position = 0;
            return mem;
        }

        public async Task CleanUpBlobChunks(string fileId)
        {
            var prefix = $"{fileId}/";
            var container = _blobStorageHelper.ChunksContainer;

            await foreach (var blobItem in container.GetBlobsByHierarchyAsync(prefix: prefix))
            {
                if (blobItem.IsBlob)
                {
                    var blobClient = container.GetBlobClient(blobItem.Blob.Name);
                    await blobClient.DeleteIfExistsAsync();
                    _logger.LogInformation("🗑️ Deleted blob: {BlobName}", blobItem.Blob.Name);
                }
            }

            _logger.LogInformation("✅ Deleted all blobs for fileId: {FileId}", fileId);
        }
    }
}