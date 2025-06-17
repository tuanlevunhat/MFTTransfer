using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Infrastructure.Constants
{
    public static class KafkaConstant
    {
        public const int DefaultBatchSize = 1000;
        public const string KafkaGroupId = "nft-group";
        public const string KafkaFileTransferInitTopic = "file-transfer-init";
        public const string KafkaFinalizeUploadChunksTopic = "finalize-upload-chunks";
        public const string KafkaChunkProcessingTopic = "chunk-processing";
        public const string KafkaTransferProgressTopic = "transfer-progress";
        public const string KafkaTransferCompleteTopic = "transfer-complete";
        public const string KafkaRetryDownloadChunkTopic = "retry-download-chunk";
        public const string KafkaStatusUpdateTopic = "status-update";
    }

    public static class KafkaNodeTopics
    {
        public const string NodeBChunkTopic = "chunk-processing-node-b";
        public const string NodeCChunkTopic = "chunk-processing-node-c";
        public const string NodeDChunkTopic = "chunk-processing-node-d";
    }
}
