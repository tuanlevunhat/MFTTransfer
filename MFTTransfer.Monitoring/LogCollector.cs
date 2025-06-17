using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using System;

namespace MFTTransfer.Monitoring
{
    /// <summary>
    /// Class to configure and collect logs for centralized logging (e.g., ELK stack).
    /// </summary>
    public static class LogCollector
    {
        public static ILogger ConfigureLogger(string elasticsearchUrl = "http://localhost:9200")
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(elasticsearchUrl))
                {
                    AutoRegisterTemplate = true,
                    AutoRegisterTemplateVersion = Serilog.Sinks.Elasticsearch.AutoRegisterTemplateVersion.ESv7,
                    IndexFormat = "mft-logs-{0:yyyy.MM.dd}"
                })
                .CreateLogger();
        }
    }
}