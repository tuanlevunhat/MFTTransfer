using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.BackgroundJobs.Helpers
{
    public interface IChunkProcessingHelperFactory
    {
        ChunkProcessingHelper Create(string nodeId, string tempFolder, string mainFolder);
    }
}
