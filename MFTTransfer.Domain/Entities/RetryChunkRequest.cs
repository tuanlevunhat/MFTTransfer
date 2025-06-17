using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Entities
{
    public class RetryChunkRequest
    {
        public string FileId { get; set; }

        public string NodeId { get; set; }

        public string ChunkId { get; set; }
    }
}
