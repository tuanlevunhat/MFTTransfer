using StackExchange.Redis;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public class RedisCacheHelper
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheHelper> _logger;

    public RedisCacheHelper(IConnectionMultiplexer redis, ILogger<RedisCacheHelper> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// Set value into Redis with optional expiry
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            if (!expiry.HasValue) expiry = TimeSpan.FromHours(1);
            var json = JsonSerializer.Serialize(value);
            await _database.StringSetAsync(key, json, expiry);
            _logger.LogDebug("✅ Cached key {Key} with expiry {Expiry}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to set Redis key {Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Get value from Redis. Returns default(T) if not found.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("❌ Key {Key} not found in Redis", key);
                return default(T);
            }

            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get Redis key {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Delete key from Redis
    /// </summary>
    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            return await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete Redis key {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Check if key exists
    /// </summary>
    public async Task<bool> ExistsAsync(string key)
    {
        return await _database.KeyExistsAsync(key);
    }

    public async Task<List<string>> GetPatternAsync(string pattern)
    {
        var keys = await _database.HashKeysAsync(pattern);
        return keys.Select(k => k.ToString().Split(':').Last()).ToList();
    }

    public async Task<List<string>> GetKeysByPatternAsync(string pattern)
    {
        var keys = await _database.HashKeysAsync(pattern);
        return keys.Select(_k => _k.ToString()).ToList();
    }
}
