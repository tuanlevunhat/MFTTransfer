using Prometheus;
using System.Diagnostics.Metrics;

namespace MFTTransfer.Monitoring
{
    /// <summary>
    /// Class to handle Prometheus metrics for monitoring file transfer process.
    /// </summary>
    public static class PrometheusMetrics
    {
        private static readonly Counter _totalFilesProcessed = Metrics.CreateCounter("mft_total_files_processed", "Total number of files processed.");
        private static readonly Counter _totalChunksProcessed = Metrics.CreateCounter("mft_total_chunks_processed", "Total number of chunks processed.");
        private static readonly Gauge _currentTransferProgress = Metrics.CreateGauge("mft_current_transfer_progress", "Current progress of file transfer (0-100).");

        /// <summary>
        /// Increments the total files processed counter.
        /// </summary>
        /// <param name="count">Number of files to increment by.</param>
        public static void IncrementFilesProcessed(int count = 1)
        {
            _totalFilesProcessed.Inc(count);
        }

        /// <summary>
        /// Increments the total chunks processed counter.
        /// </summary>
        /// <param name="count">Number of chunks to increment by.</param>
        public static void IncrementChunksProcessed(int count = 1)
        {
            _totalChunksProcessed.Inc(count);
        }

        /// <summary>
        /// Sets the current transfer progress.
        /// </summary>
        /// <param name="progress">Progress value (0-100).</param>
        public static void SetTransferProgress(double progress)
        {
            _currentTransferProgress.Set(Math.Max(0, Math.Min(100, progress)));
        }
    }
}