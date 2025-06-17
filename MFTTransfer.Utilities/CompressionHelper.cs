using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Core;
using System.IO;

namespace MFTTransfer.Utilities
{
    /// <summary>
    /// Helper class to handle file compression (optional) for chunking process.
    /// </summary>
    public static class CompressionHelper
    {
        /// <summary>
        /// Compresses a file into a ZIP stream.
        /// </summary>
        /// <param name="filePath">Path to the source file.</param>
        /// <param name="compressionLevel">Level of compression (0-9).</param>
        /// <returns>MemoryStream containing the compressed data.</returns>
        public static MemoryStream CompressFile(string filePath, int compressionLevel = 6)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var memoryStream = new MemoryStream();
            using (var zipOutputStream = new ZipOutputStream(memoryStream))
            {
                zipOutputStream.SetLevel(compressionLevel); // 0-9, 6 is default
                var entry = new ZipEntry(Path.GetFileName(filePath))
                {
                    DateTime = DateTime.Now
                };
                zipOutputStream.PutNextEntry(entry);

                byte[] buffer = new byte[4096];
                StreamUtils.Copy(fileStream, zipOutputStream, buffer);
                zipOutputStream.CloseEntry();
            }

            memoryStream.Position = 0;
            return memoryStream;
        }

        /// <summary>
        /// Decompresses a ZIP stream back to a file or memory.
        /// </summary>
        /// <param name="compressedStream">Stream containing compressed data.</param>
        /// <param name="outputPath">Optional path to save decompressed file.</param>
        /// <returns>MemoryStream of decompressed data if no output path is provided.</returns>
        public static MemoryStream DecompressStream(Stream compressedStream, string outputPath = null)
        {
            var memoryStream = new MemoryStream();
            using (var zipInputStream = new ZipInputStream(compressedStream))
            {
                var entry = zipInputStream.GetNextEntry();
                if (entry == null)
                    throw new InvalidDataException("No entries found in the ZIP stream.");

                byte[] buffer = new byte[4096];
                Stream outputStream = outputPath != null ? new FileStream(outputPath, FileMode.Create) : memoryStream;

                StreamUtils.Copy(zipInputStream, outputStream, buffer);
                outputStream.Flush();

                if (outputPath != null)
                    outputStream.Close();
            }

            if (outputPath == null)
            {
                memoryStream.Position = 0;
                return memoryStream;
            }
            return null;
        }
    }
}