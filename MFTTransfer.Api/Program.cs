using MFTTransfer.Domain.Interfaces;
using MFTTransfer.Infrastructure;
using MFTTransfer.Infrastructure.Helpers;
using MFTTransfer.Infrastructure.Services;
using Serilog;
using MFTTransfer.Infrastructure.Services.Kafka;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", builder =>
    {
        builder.WithOrigins("http://localhost:5173")
               .AllowAnyHeader()
               .AllowAnyMethod()
               .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddHostedService<KafkaConsumerCoordinator>();
builder.Services.AddSingleton<KafkaTopicManager>();
builder.Services.AddSingleton<BaseProducerService>();
builder.Services.AddOpenApi();
builder.Services.AddScoped<BlobStorageHelper>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<IKafkaService, KafkaService>();
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<FileTransferService>();
builder.Services.AddLogging(logging => logging.AddConsole());
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["DistributedCacheConfig:ConnectionString"];
});
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    return ConnectionMultiplexer.Connect(builder.Configuration["DistributedCacheConfig:ConnectionString"]);
});

builder.Services.AddSingleton<RedisCacheHelper>();
builder.Services.AddHttpClient();
builder.Services.AddLogging(logging => logging.AddSerilog());
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting();
app.UseCors("AllowReact");
app.UseAuthorization();
app.MapControllers();

app.Run();