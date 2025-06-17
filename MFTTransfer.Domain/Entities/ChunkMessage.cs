using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Entities
{
    public class ChunkMessage
    {
        public string FileId { get; set; }
        public string FullFileName { get; set; }
        public string ChunkId { get; set; }
        public int ChunkIndex { get; set; }
        public string BlobUrl { get; set; }
        public long Size { get; set; } // Chunk size (bytes)
        public string Checksum { get; set; }  // Checksum (MD5/SHA256)
        public int TotalChunks { get; set; }
        public bool IsLast { get; set; }
    }
}