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

namespace MFTTransfer.BackgroundJobs.Consumer.Nodes
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

        public ChunkProcessingHelper(ILogger logger,
            string nodeId,
            string tempFolder,
            string mainFolder,
            IHttpClientFactory httpClientFactory,
            IRedisService redisService,
            IKafkaService kafkaService)
        {
            _logger = logger;
            _nodeId = nodeId;
            _tempFolder = tempFolder;
            _mainFolder = mainFolder;
            _httpClientFactory = httpClientFactory;
            _redisService = redisService;
            _kafkaService = kafkaService;
        }

        public async Task ProcessingChunk(string message, CancellationToken cancellationToken)
        {
            var chunk = JsonSerializer.Deserialize<ChunkMessage>(message);
            if (chunk == null || string.IsNullOrWhiteSpace(chunk.ChunkId))
            {
                _logger.LogWarning("⚠️ Invalid chunk message received: {Message}", message);
                return;
            }

            _logger.LogInformation("📦 Node {nodeId} received chunk {ChunkId} for file {FileId}", _nodeId, chunk.ChunkId, chunk.FileId);

            try
            {
                var policy = Policy.Handle<Exception>().RetryAsync(3, onRetry: (ex, count) =>
                    _logger.LogWarning(ex, "Retry {RetryCount} for chunk {ChunkId}", count, chunk.ChunkId));

                await policy.ExecuteAsync(async () =>
                {
                    using var client = _httpClientFactory.CreateClient();
                    using var response = await client.GetAsync(chunk.BlobUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    _logger.LogInformation("📥 Downloaded {Bytes} bytes for chunk {ChunkId}", data.Length, chunk.ChunkId);

                    if (chunk.Size != 0 && data.Length != chunk.Size)
                    {
                        _logger.LogWarning("❗ Chunk {ChunkId} size mismatch. Expected {Expected}, got {Actual}", chunk.ChunkId, chunk.Size, data.Length);
                        return;
                    }

                    var chunkFolder = Path.Combine(_tempFolder, chunk.FileId);
                    Directory.CreateDirectory(chunkFolder);
                    var chunkPath = Path.Combine(chunkFolder, chunk.ChunkId);
                    await File.WriteAllBytesAsync(chunkPath, data, cancellationToken);
                    await _redisService.SetProcessedChunks(chunk.FileId, _nodeId);
                    if ((int)(((float)(chunk.ChunkIndex + 1) / (float)chunk.TotalChunks) * 100) % 5 == 0)
                    {
                        await _kafkaService.SendTransferProgressMessage(chunk.FileId, new TransferProgressMessage()
                        {
                            FileId = chunk.FileId,
                            NodeId = _nodeId,
                            Progress = (int)(((float)(chunk.ChunkIndex + 1) / (float)chunk.TotalChunks) * 100),
                            Status = "InProgress"
                        });
                    }
                    var total = chunk.TotalChunks;
                    var current = await _redisService.GetProcessedChunks(chunk.FileId, _nodeId);

                    if (current == total)
                    {
                        _logger.LogInformation("🎉 Node {_nodeId} completed all chunks for file {FileId}", _nodeId, chunk.FileId);
                        await MergeChunks(chunk.FullFileName, chunk.FileId, current);
                        await _kafkaService.SendTransferProgressMessage(chunk.FileId, new TransferProgressMessage()
                        {
                            FileId = chunk.FileId,
                            NodeId = _nodeId,
                            Progress = 100,
                            Status = "Completed"
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error handling chunk {ChunkId}");
            }
        }

        public async Task ProcessChunksConcurrently(IEnumerable<ChunkMessage> chunks, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateUnbounded<ChunkResult>();
            var semaphore = new SemaphoreSlim(2);

            var writerTasks = chunks.Select(async chunk =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await DownloadChunkWithRetry(chunk, cancellationToken);
                    if (result != null)
                    {
                        await channel.Writer.WriteAsync(result, cancellationToken);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var readerTask = ProcessChannelAndMerge(channel, cancellationToken);

            await Task.WhenAll(writerTasks);
            channel.Writer.Complete();
            await readerTask;
        }

        private async Task<ChunkResult?> DownloadChunkWithRetry(ChunkMessage chunk, CancellationToken cancellationToken)
        {
            _logger.LogInformation("📦 Node {nodeId} received chunk {ChunkId} for file {FileId}", _nodeId, chunk.ChunkId, chunk.FileId);
            var policy = Policy.Handle<Exception>().RetryAsync(3, onRetry: (ex, count) =>
                _logger.LogWarning(ex, "🔁 Retry {Count} for chunk {ChunkId}", count, chunk.ChunkId));

            return await policy.ExecuteAsync(async () =>
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(chunk.BlobUrl, cancellationToken);
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

                        await _redisService.MarkDownloadChunkFailed(chunk.FileId, _nodeId, chunk.ChunkId);
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

        private async Task ProcessChannelAndMerge(Channel<ChunkResult> channel, CancellationToken cancellationToken)
        {
            var chunkGroups = new Dictionary<string, List<ChunkResult>>();

            await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var chunkDir = Path.Combine(_tempFolder, result.FileId);
                Directory.CreateDirectory(chunkDir);
                var chunkPath = Path.Combine(chunkDir, result.ChunkId);
                await File.WriteAllBytesAsync(chunkPath, result.Data, cancellationToken);

                await _redisService.SetProcessedChunks(result.FileId, _nodeId);
                await _redisService.ClearChunkResumeOffset(result.FileId, _nodeId, result.ChunkId);

                if (!chunkGroups.TryGetValue(result.FileId, out var list))
                {
                    list = new List<ChunkResult>();
                    chunkGroups[result.FileId] = list;
                }
                list.Add(result);

                var processedChunks = await _redisService.GetProcessedChunks(result.FileId, _nodeId);
                if (processedChunks == result.TotalChunks)
                {
                    _logger.LogInformation("🎉 All chunks received for file {FileId}, merging...", result.FileId);
                    await MergeChunks(result.FileName, result.FileId, result.TotalChunks);
                    chunkGroups.Remove(result.FileId);
                    await _kafkaService.SendTransferProgressMessage(result.FileId, new TransferProgressMessage()
                    {
                        FileId = result.FileId,
                        NodeId = _nodeId,
                        Progress = 100,
                        Status = "Completed"
                    });
                }
                else
                {
                    var percent = (int)(((float)(processedChunks) / (float)result.TotalChunks) * 100);
                    _logger.LogInformation("🎉 Node {nodeId} received  {percent} for file {FileId}", _nodeId, percent, result.FileId);
                    if (percent % 5 == 0)
                    {
                        await _kafkaService.SendTransferProgressMessage(result.FileId, new TransferProgressMessage()
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

        public async Task MergeChunks(string fileName, string fileId, int totalChunks)
        {
            var fileTempDir = Path.Combine(_tempFolder, fileId);
            if (!Directory.Exists(_mainFolder)) Directory.CreateDirectory(_mainFolder);

            var outputPath = Path.Combine(_mainFolder, fileName);
            using var output = new FileStream(outputPath, FileMode.Create);

            for (int i = 0; i < totalChunks; i++)
            {
                var chunkPath = Path.Combine(fileTempDir, $"chunk_{i}");
                if (!File.Exists(chunkPath)) throw new FileNotFoundException($"Missing chunk: {chunkPath}");

                var data = await File.ReadAllBytesAsync(chunkPath);
                await output.WriteAsync(data);
            }

            Directory.Delete(fileTempDir, true);
            _logger.LogInformation("📦 Merged file saved to {Output}", outputPath);

            await _redisService.MarkNodeTransferComplete(fileId, _nodeId);
            await _kafkaService.SendTransferCompleteMessage(fileId, new TransferProgressMessage()
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

        public async Task RequestRetryForResumableChunks(string fileId, CancellationToken cancellationToken)
        {
            var resumableChunks = await _redisService.GetAllResumableChunks(fileId, _nodeId);

            foreach (var chunkId in resumableChunks)
            {
                _logger.LogInformation("📣 Requesting retry for chunk {ChunkId} (file {FileId})", chunkId, fileId);

                await _kafkaService.SendRetryChunkRequest(new RetryChunkRequest
                {
                    FileId = fileId,
                    ChunkId = chunkId,
                    NodeId = _nodeId
                });
            }
        }

        public async Task HandleRetryChunkRequest(RetryChunkRequest request, CancellationToken cancellationToken)
        {
            var chunkMetadata = await _redisService.GetChunkMetadata(request.FileId, request.ChunkId);
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
                    var result = await ResumeChunkDownload(request.NodeId, chunkMessage, cancellationToken);
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

            await ProcessChannelAndMerge(channel, cancellationToken);
        }

        private async Task<ChunkResult?> ResumeChunkDownload(string nodeId, ChunkMessage chunk, CancellationToken cancellationToken)
        {
            var offset = await _redisService.GetChunkResumeOffset(chunk.FileId, nodeId, chunk.ChunkId) ?? 0;

            var policy = Policy.Handle<Exception>().RetryAsync(3, onRetry: (ex, count) =>
                _logger.LogWarning(ex, "🔁 Retry {Count} for chunk {ChunkId}", count, chunk.ChunkId));

            return await policy.ExecuteAsync(async () =>
            {
                using var client = _httpClientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Get, chunk.BlobUrl);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await _redisService.SetChunkResumeOffset(chunk.FileId, nodeId, chunk.ChunkId, offset + data.Length);

                return new ChunkResult(chunk.ChunkId, chunk.FileId, chunk.FullFileName, chunk.ChunkIndex, chunk.TotalChunks, data);
            });
        }
    }

    public record ChunkResult(string ChunkId, string FileId, string FileName, int ChunkIndex, int TotalChunks, byte[] Data);
}
