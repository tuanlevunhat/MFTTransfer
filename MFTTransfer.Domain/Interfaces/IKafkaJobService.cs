using System.Threading;
using System.Threading.Tasks;

namespace MFTTransfer.Domain.Interfaces
{
    /// <summary>
    /// Interface định nghĩa các phương thức xử lý job nền sử dụng Kafka.
    /// </summary>
    public interface IKafkaJobService
    {
        /// <summary>
        /// Khởi động service để xử lý job (nếu cần).
        /// </summary>
        /// <param name="cancellationToken">Token để hủy xử lý.</param>
        /// <returns>Task đại diện cho hoạt động khởi động.</returns>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Xử lý job liên quan đến chunk từ message Kafka.
        /// </summary>
        /// <param name="message">Nội dung message từ Kafka (dạng JSON).</param>
        /// <returns>Task đại diện cho hoạt động xử lý.</returns>
        Task ProcessChunkJob(string message);

        /// <summary>
        /// Xử lý job cập nhật trạng thái từ message Kafka.
        /// </summary>
        /// <param name="message">Nội dung message từ Kafka (dạng JSON).</param>
        /// <returns>Task đại diện cho hoạt động xử lý.</returns>
        Task ProcessStatusUpdateJob(string message);
    }
}