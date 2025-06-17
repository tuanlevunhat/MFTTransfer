using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using System;

namespace MFTTransfer.Infrastructure.Helpers
{
    public class BlobStorageHelper
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly BlobContainerClient _chunksContainer;
        private readonly BlobContainerClient _completedContainer;
        private readonly BlobContainerClient _metadataContainer;
        private readonly BlobContainerClient _sourceContainer;
        private readonly ILogger<BlobStorageHelper> _logger;

        public BlobStorageHelper(IConfiguration configuration, ILogger<BlobStorageHelper> logger)
        {
            var connectionString = configuration.GetSection("BlobStorage")?["ConnectionString"]
                                   ?? throw new ArgumentNullException("Missing BlobStorage:ConnectionString");

            _blobServiceClient = new BlobServiceClient(connectionString);
            _logger = logger;

            var sourceContainerName = configuration["BlobStorage:SourceContainer"] ?? "nft-source";
            var chunksContainerName = configuration["BlobStorage:ChunksContainer"] ?? "nft-chunks";
            var completedContainerName = configuration["BlobStorage:CompletedContainer"] ?? "nft-completed";
            var metadataContainerName = configuration["BlobStorage:MetadataContainer"] ?? "nft-metadata";

            _sourceContainer = _blobServiceClient.GetBlobContainerClient(sourceContainerName);
            _chunksContainer = _blobServiceClient.GetBlobContainerClient(chunksContainerName);
            _completedContainer = _blobServiceClient.GetBlobContainerClient(completedContainerName);
            _metadataContainer = _blobServiceClient.GetBlobContainerClient(metadataContainerName);
        }

        public async Task EnsureContainersExistAsync()
        {
            await Task.WhenAll(
                _sourceContainer.CreateIfNotExistsAsync(PublicAccessType.None),
                _chunksContainer.CreateIfNotExistsAsync(PublicAccessType.None),
                _completedContainer.CreateIfNotExistsAsync(PublicAccessType.None),
                _metadataContainer.CreateIfNotExistsAsync(PublicAccessType.None)
            );
        }

        public async Task StageBlockAsync(BlobContainerClient container, string blobName, string blockId, Stream content)
        {
            try
            {
                var blockClient = container.GetBlockBlobClient(blobName);
                var policy = Policy
                    .Handle<Azure.RequestFailedException>(ex => ex.ErrorCode != "InvalidBlobOrBlock")
                    .WaitAndRetryAsync(3, 
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), 
                        onRetry: (exception, retryCount) =>
                        {
                            Console.WriteLine($"Retry {retryCount} failed: {exception.Message}");
                        }
                    );

                await policy.ExecuteAsync(async () =>
                {
                    if (content.CanSeek)
                        content.Position = 0;
                    _logger.LogDebug("📦 Staging block {BlockId} — Position: {Pos}, Length: {Len}", blockId, content.Position, content.Length);
                    await blockClient.StageBlockAsync(blockId, content);
                });

                _logger?.LogDebug("✅ Staged block {BlockId} for blob {BlobName}", blockId, blobName);
            }
            catch (RequestFailedException ex)
            {
                _logger?.LogError(ex, "❌ Azure error staging block {BlockId} for blob {BlobName}", blockId, blobName);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Unknown error staging block {BlockId} for blob {BlobName}", blockId, blobName);
                throw;
            }
        }

        public async Task CommitBlockListAsync(BlobContainerClient container, string blobName, IEnumerable<string> blockIds)
        {
            try
            {
                var blockClient = container.GetBlockBlobClient(blobName);
                await blockClient.CommitBlockListAsync(blockIds.ToList());
                _logger?.LogInformation("✅ Committed block list for blob {BlobName}", blobName);
            }
            catch (RequestFailedException ex)
            {
                _logger?.LogError(ex, "❌ Failed to commit block list for blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task UploadBlobAsync(BlobContainerClient container, string blobName, Stream content)
        {
            try
            {
                var blobClient = container.GetBlockBlobClient(blobName);
                content.Position = 0;
                await blobClient.UploadAsync(content);
                _logger?.LogInformation("✅ Uploaded blob {BlobName}", blobName);
            }
            catch (RequestFailedException ex)
            {
                _logger?.LogError(ex, "❌ Failed to upload blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task<Stream?> DownloadBlobAsync(BlobContainerClient container, string blobName)
        {
            try
            {
                var blobClient = container.GetBlockBlobClient(blobName);
                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    _logger?.LogInformation("✅ Downloaded blob {BlobName}", blobName);
                    return response.Value.Content;
                }

                _logger?.LogWarning("⚠️ Blob {BlobName} does not exist", blobName);
                return null;
            }
            catch (RequestFailedException ex)
            {
                _logger?.LogError(ex, "❌ Failed to download blob {BlobName}", blobName);
                throw;
            }
        }

        public async Task DeleteBlobAsync(BlobContainerClient container, string blobName)
        {
            try
            {
                var blobClient = container.GetBlockBlobClient(blobName);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted)
                    _logger?.LogInformation("🗑️ Deleted blob {BlobName}", blobName);
                else
                    _logger?.LogWarning("⚠️ Blob {BlobName} did not exist", blobName);
            }
            catch (RequestFailedException ex)
            {
                _logger?.LogError(ex, "❌ Failed to delete blob {BlobName}", blobName);
                throw;
            }
        }

        public BlobContainerClient ChunksContainer => _chunksContainer;
        public BlobContainerClient CompletedContainer => _completedContainer;
        public BlobContainerClient MetadataContainer => _metadataContainer;
        public BlobContainerClient SourceContainer => _sourceContainer;
    }
}