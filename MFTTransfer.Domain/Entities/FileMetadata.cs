using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Entities
{
    public class FileMetadata
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public List<ChunkMetadata> Chunks { get; set; }
        public long TotalSize { get; set; }
        public string OverallChecksum { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
