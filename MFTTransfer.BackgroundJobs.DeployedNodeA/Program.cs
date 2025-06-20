using MFTTransfer.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MFTTransfer.Monitoring;
using MFTTransfer.Infrastructure.Helpers;
using Microsoft.Extensions.Configuration;
using Serilog;
using MFTTransfer.Infrastructure.Services.Kafka;
using MFTTransfer.Infrastructure.Services;
using StackExchange.Redis;
using MFTTransfer.Infrastructure;
using MFTTransfer.BackgroundJobs.Services;
using MFTTransfer.BackgroundJobs.Consumer;
using MFTTransfer.BackgroundJobs.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .MinimumLevel.Debug()
    .CreateLogger();
Log.Information("Starting up...");
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddSingleton<KafkaTopicManager>();
        services.AddSingleton<AppInitializer>();
        services.AddSingleton<BaseProducerService>();
        services.AddSingleton<IKafkaMessageHandler, InitTransferHandler>();
        services.AddSingleton<IKafkaMessageHandler, ChunkProcessingHandler>();
        services.AddSingleton<IKafkaMessageHandler, TransferCompleteHandler>();
        services.AddSingleton<IKafkaMessageHandler, StatusUpdateHandler>();
        services.AddSingleton<IKafkaMessageHandler, RetryDownloadChunkHandler>();
        services.AddHostedService<KafkaConsumerCoordinator>();
        services.AddScoped<BlobStorageHelper>();
        services.AddScoped<IRedisService, RedisService>();
        services.AddScoped<IKafkaService, KafkaService>();
        services.AddScoped<IBlobService, BlobService>();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = hostContext.Configuration["DistributedCacheConfig:ConnectionString"];
        });
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            return ConnectionMultiplexer.Connect(hostContext.Configuration["DistributedCacheConfig:ConnectionString"]);
        });

        services.AddTransient<IChunkProcessingHelperFactory, ChunkProcessingHelperFactory>();
        services.AddSingleton<RedisCacheHelper>();
        services.AddHttpClient(hostContext.Configuration["NodeSettings:NodeId"], client =>
        {
            client.Timeout = TimeSpan.FromHours(1);
        })
        .SetHandlerLifetime(TimeSpan.FromHours(1));
        services.AddHostedService<ChunkRetryMonitorService>();
    })
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .UseSerilog()
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
    })
    .Build();

using var scope = host.Services.CreateScope();
var initializer = scope.ServiceProvider.GetRequiredService<AppInitializer>();
await initializer.InitKafkaTopicsAsync();

await host.RunAsync();