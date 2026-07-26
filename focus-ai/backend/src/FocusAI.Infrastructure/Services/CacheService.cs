using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Services;

/// <summary>
/// JSON-over-IDistributedCache. Backed by Redis in production and by the
/// in-memory distributed cache in development, so nothing has to change to run
/// without Redis.
/// </summary>
public sealed class CacheService(
    IDistributedCache cache,
    ILogger<CacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await cache.GetStringAsync(key, cancellationToken);
            return string.IsNullOrEmpty(payload)
                ? default
                : JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A cache outage should slow the app down, not break it.
            logger.LogWarning(ex, "FocusAI cache read failed for {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(value, JsonOptions);
            await cache.SetStringAsync(
                key,
                payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI cache eviction failed for {Key}", key);
        }
    }
}
