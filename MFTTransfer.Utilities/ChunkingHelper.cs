using MFTTransfer.Domain;
using MFTTransfer.Domain.Entities;
using System.IO;

namespace MFTTransfer.Utilities
{
    /// <summary>
    /// Helper class to split a file into chunks for upload.
    /// </summary>
    public static class ChunkingHelper
    {
        /// <summary>
        /// Splits a file into chunks of specified size (default 10MB).
        /// </summary>
        /// <param name="filePath">Path to the source file.</param>
        /// <param name="chunkSize">Size of each chunk in bytes (default 10MB).</param>
        /// <param fileId">Unique ID for the file.</param>
        /// <returns>List of ChunkMetadata for each chunk.</returns>
        public static List<ChunkMetadata> SplitIntoChunks(string filePath, long chunkSize = 10 * 1024 * 1024, string fileId = null, string chunkContainer = null)
        {
            var chunkMetadatas = new List<ChunkMetadata>();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long fileSize = fileStream.Length;
            int chunkId = 0;

            for (long i = 0; i < fileSize; i += chunkSize)
            {
                long remainingBytes = fileSize - i;
                long currentChunkSize = Math.Min(chunkSize, remainingBytes);
                var chunkStream = new MemoryStream();
                fileStream.CopyTo(chunkStream, (int)currentChunkSize);
                chunkStream.Position = 0;

                var chunkMetadata = new ChunkMetadata
                {
                    ChunkId = $"chunk_{chunkId:D3}", // e.g., chunk_001
                    Size = currentChunkSize,
                    Checksum = ComputeChecksum(chunkStream) // Implement checksum logic
                };
                if (!string.IsNullOrEmpty(fileId))
                    chunkMetadata.BlobUrl = $"{chunkContainer}/{fileId}/{chunkMetadata.ChunkId}"; // Placeholder URL

                chunkMetadatas.Add(chunkMetadata);
                chunkId++;
            }

            return chunkMetadatas;
        }

        public static string ComputeChecksum(Stream stream)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            stream.Position = 0;
            var hash = md5.ComputeHash(stream);
            return Convert.ToBase64String(hash);
        }
    }
}