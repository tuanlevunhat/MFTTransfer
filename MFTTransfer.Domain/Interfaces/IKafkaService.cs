using MFTTransfer.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Interfaces
{
    public interface IKafkaService
    {
        Task SendInitTransferMessage(FileTransferInitMessage message);
        Task SendChunkMessageToNode(string topic, ChunkMessage chunkMessage);
        Task SendChunkMessage(string fileId, ChunkMessage message);
        Task SendTransferCompleteMessage(string fileId, TransferProgressMessage message);
        Task SendTransferProgressMessage(string fileId, TransferProgressMessage message);
        Task SendFinalizeUploadChunksMessage(string fileId);
        Task SendRetryChunkRequest(RetryChunkRequest request);
    }
}
