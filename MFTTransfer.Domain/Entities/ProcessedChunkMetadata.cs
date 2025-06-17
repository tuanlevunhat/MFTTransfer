using MFTTransfer.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Entities
{
    public class ProcessedChunkMetadata : ChunkMessage
    {
        public ProcessedChunkStatus Status { get; set; }
    }
}
