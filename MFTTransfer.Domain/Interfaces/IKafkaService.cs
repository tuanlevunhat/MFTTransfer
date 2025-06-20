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
        Task SendInitTransferMessageAsync(string topic, FileTransferInitMessage message);
        Task SendChunkMessageAsync(string topic, ChunkMessage chunkMessage);
        Task SendChunkBatchMessageAsync(string topic, string fileId, List<ChunkMessage> chunkMessages);
        Task SendChunkMessage(string fileId, ChunkMessage message);
        Task SendTransferCompleteMessageAsync(string topic, string fileId, TransferProgressMessage message);
        Task SendTransferProgressMessageAsync(string fileId, TransferProgressMessage message);
        Task SendFinalizeUploadChunksMessage(string fileId);
        Task SendRetryChunkMessageAsync(RetryChunkRequest request);
    }
}
