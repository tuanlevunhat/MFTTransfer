using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure.Helpers;
using MFTTransfer.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs.Helpers
{
    public class ChunkProcessingHelper
    {
        private readonly ILogger _logger;
        private readonly string _nodeId;
        private readonly string _tempFolder;
        private readonly string _mainFolder;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRedisService _redisService;
        private readonly IKafkaService _kafkaService;
        private readonly IConfiguration _configuration;

        public ChunkProcessingHelper(ILogger logger,
            string nodeId,
            string tempFolder,
            string mainFolder,
            IHttpClientFactory httpClientFactory,
            IRedisService redisService,
            IKafkaService kafkaService,
            IConfiguration configuration)
        {
            _logger = logger;
            _nodeId = nodeId;
            _tempFolder = tempFolder;
            _mainFolder = mainFolder;
            _httpClientFactory = httpClientFactory;
            _redisService = redisService;
            _kafkaService = kafkaService;
            _configuration = configuration;
        }

        public async Task ProcessChunksConcurrentlyAsync(IEnumerable<ChunkMessage> chunks, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<ChunkResult>();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = int.Parse(_configuration["MaxDegreeOfParallelism"] ?? "4"),
                CancellationToken = cancellationToken
            };

            var writerTask = Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, token) =>
            {
                try
                {
                    var result = await DownloadChunkWithRetryAsync(chunk, token);
                    if (result != null)
                    {
                        await channel.Writer.WriteAsync(result, token);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Chunk {ChunkId} download returned null", chunk.ChunkId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Unexpected error while downloading chunk {ChunkId}", chunk.ChunkId);
                    await _redisService.MarkDownloadChunkFailedAsync(chunk.FileId, _nodeId, chunk.ChunkId);
                }
            });

            var readerTask = ProcessChannelAndMergeAsync(channel, cancellationToken);

            await writerTask;
            channel.Writer.Complete();
            await readerTask;
        }

        private async Task<ChunkResult?> DownloadChunkWithRetryAsync(ChunkMessage chunk, CancellationToken cancellationToken)
        {
            _logger.LogInformation("📦 Node {nodeId} received chunk {ChunkId} for file {FileId}", _nodeId, chunk.ChunkId, chunk.FileId);
            var policy = Policy.Handle<Exception>().WaitAndRetryAsync(
                int.Parse(_configuration["MaxRetryCount"] ?? "3"),
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, count) => _logger.LogWarning(ex, "🔁 Retry {Count} for chunk {ChunkId}", count, chunk.ChunkId));

            return await policy.ExecuteAsync(async () =>
            {
                using var client = _httpClientFactory.CreateClient(_configuration["NodeSettings:NodeId"]);
                using var response = await client.GetAsync(chunk.BlobUrl, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HTTP {StatusCode} when downloading chunk {ChunkId}", response.StatusCode, chunk.ChunkId);
                }

                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                _logger.LogInformation("📥 Downloaded {Bytes} bytes for chunk {ChunkId}", data.Length, chunk.ChunkId);
                // Check checksum
                if (!string.IsNullOrEmpty(chunk.Checksum))
                {
                    var calculated = ComputeSha256(data);
                    if (!string.Equals(calculated, chunk.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError("❌ Checksum mismatch for chunk {ChunkId}. Expected {Expected}, got {Actual}",
                            chunk.ChunkId, chunk.Checksum, calculated);

                        await _redisService.MarkDownloadChunkFailedAsync(chunk.FileId, _nodeId, chunk.ChunkId);
                        return null;
                    }
                }

                if (chunk.Size != 0 && data.Length != chunk.Size)
                {
                    _logger.LogWarning("❗ Size mismatch for chunk {ChunkId}: expected {Expected}, got {Actual}", chunk.ChunkId, chunk.Size, data.Length);
                    return null;
                }

                return new ChunkResult(chunk.ChunkId, chunk.FileId, chunk.FullFileName, chunk.ChunkIndex, chunk.TotalChunks, data);
            });
        }

        private async Task ProcessChannelAndMergeAsync(Channel<ChunkResult> channel, CancellationToken cancellationToken)
        {
            var chunkGroups = new Dictionary<string, List<ChunkResult>>();

            await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var chunkDir = Path.Combine(_tempFolder, result.FileId);
                Directory.CreateDirectory(chunkDir);
                var chunkPath = Path.Combine(chunkDir, result.ChunkId);
                await File.WriteAllBytesAsync(chunkPath, result.Data, cancellationToken);

                await _redisService.SetProcessedChunksAsync(result.FileId, _nodeId);
                await _redisService.ClearChunkResumeOffsetAsync(result.FileId, _nodeId, result.ChunkId);

                if (!chunkGroups.TryGetValue(result.FileId, out var list))
                {
                    list = new List<ChunkResult>();
                    chunkGroups[result.FileId] = list;
                }
                list.Add(result);

                var processedChunks = await _redisService.GetProcessedChunksAsync(result.FileId, _nodeId);
                if (processedChunks == result.TotalChunks)
                {
                    _logger.LogInformation("🎉 All chunks received for file {FileId}, merging...", result.FileId);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await MergeChunksAsync(result.FileName, result.FileId, result.TotalChunks);
                            await _kafkaService.SendTransferProgressMessageAsync(result.FileId, new TransferProgressMessage()
                            {
                                FileId = result.FileId,
                                NodeId = _nodeId,
                                Progress = 100,
                                Status = "Completed"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ Error while merging file {FileId}", result.FileId);
                        }
                    });

                    chunkGroups.Remove(result.FileId);
                }
                else
                {
                    var percent = (int)(processedChunks / (float)result.TotalChunks * 100);
                    _logger.LogInformation("🎉 Node {nodeId} received  {percent} for file {FileId}", _nodeId, percent, result.FileId);
                    if (percent % 5 == 0)
                    {
                        await _kafkaService.SendTransferProgressMessageAsync(result.FileId, new TransferProgressMessage()
                        {
                            FileId = result.FileId,
                            NodeId = _nodeId,
                            Progress = percent,
                            Status = "InProgress"
                        });
                    }
                }
            }
        }

        public async Task MergeChunksAsync(string fileName, string fileId, int totalChunks)
        {
            var fileTempDir = Path.Combine(_tempFolder, fileId);
            if (!Directory.Exists(_mainFolder)) Directory.CreateDirectory(_mainFolder);

            var outputPath = Path.Combine(_mainFolder, fileName);
            using var output = new FileStream(outputPath, FileMode.Create);

            for (int i = 0; i < totalChunks; i++)
            {
                var chunkPath = Path.Combine(fileTempDir, $"chunk_{i}");
                if (!File.Exists(chunkPath))
                {
                    _logger.LogError("❌ Missing chunk file: {ChunkPath}", chunkPath);
                    throw new FileNotFoundException($"Missing chunk: {chunkPath}");
                }

                using var chunkStream = new FileStream(chunkPath, FileMode.Open, FileAccess.Read);
                await chunkStream.CopyToAsync(output);
            }

            Directory.Delete(fileTempDir, true);
            _logger.LogInformation("📦 Merged file saved to {Output}", outputPath);
            await _redisService.MarkNodeTransferCompleteAsync(fileId, _nodeId);
            var transferNode = await _redisService.GetTransferNodeAsync(fileId);
            var topic = string.Concat(_configuration["Kafka:Topic:TransferComplete"], "_", transferNode);
            await _kafkaService.SendTransferCompleteMessageAsync(topic, fileId, new TransferProgressMessage
            {
                FileId = fileId,
                NodeId = _nodeId,
                Progress = 100,
                Status = "Completed"
            });
        }

        public static string ComputeSha256(byte[] data)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public async Task RequestRetryForResumableChunksAsync(string fileId, CancellationToken cancellationToken)
        {
            var resumableChunks = await _redisService.GetAllResumableChunksAsync(fileId, _nodeId);

            foreach (var chunkId in resumableChunks)
            {
                _logger.LogInformation("📣 Requesting retry for chunk {ChunkId} (file {FileId})", chunkId, fileId);

                await _kafkaService.SendRetryChunkMessageAsync(new RetryChunkRequest
                {
                    FileId = fileId,
                    ChunkId = chunkId,
                    NodeId = _nodeId
                });
            }
        }

        public async Task HandleRetryChunkAsync(RetryChunkRequest request, CancellationToken cancellationToken)
        {
            var chunkMetadata = await _redisService.GetChunkMetadataAsync(request.FileId, request.ChunkId);
            var chunkMessage = new ChunkMessage
            {
                FileId = request.FileId,
                ChunkId = request.ChunkId,
                BlobUrl = chunkMetadata.BlobUrl,
                Checksum = chunkMetadata.Checksum,
                ChunkIndex = chunkMetadata.ChunkIndex,
                FullFileName = chunkMetadata.FullFileName,
                Size = chunkMetadata.Size,
                TotalChunks = chunkMetadata.TotalChunks
            };

            var channel = Channel.CreateUnbounded<ChunkResult>();
            var semaphore = new SemaphoreSlim(4);

            await semaphore.WaitAsync(cancellationToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await ResumeChunkDownloadAsync(request.NodeId, chunkMessage, cancellationToken);
                    if (result != null)
                    {
                        await channel.Writer.WriteAsync(result, cancellationToken);
                    }
                }
                finally
                {
                    semaphore.Release();
                    channel.Writer.Complete();
                }
            });

            await ProcessChannelAndMergeAsync(channel, cancellationToken);
        }

        private async Task<ChunkResult?> ResumeChunkDownloadAsync(string nodeId, ChunkMessage chunk, CancellationToken cancellationToken)
        {
            var offset = await _redisService.GetChunkResumeOffsetAsync(chunk.FileId, nodeId, chunk.ChunkId) ?? 0;

            var policy = Policy.Handle<Exception>().WaitAndRetryAsync(
                int.Parse(_configuration["MaxRetryCount"] ?? "3"),
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (ex, count) => _logger.LogWarning(ex, "🔁 Retry {Count} for chunk {ChunkId}", count, chunk.ChunkId));

            return await policy.ExecuteAsync(async () =>
            {
                using var client = _httpClientFactory.CreateClient(_configuration["NodeSettings:NodeId"]);
                var request = new HttpRequestMessage(HttpMethod.Get, chunk.BlobUrl);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("❌ HTTP {StatusCode} when downloading chunk {ChunkId}", response.StatusCode, chunk.ChunkId);
                }

                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await _redisService.SetChunkResumeOffsetAsync(chunk.FileId, nodeId, chunk.ChunkId, offset + data.Length);

                return new ChunkResult(chunk.ChunkId, chunk.FileId, chunk.FullFileName, chunk.ChunkIndex, chunk.TotalChunks, data);
            });
        }
    }

    public record ChunkResult(string ChunkId, string FileId, string FileName, int ChunkIndex, int TotalChunks, byte[] Data);
}
