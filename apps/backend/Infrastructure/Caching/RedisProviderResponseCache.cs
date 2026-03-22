using System.Text.Json;
using backend.Infrastructure.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace backend.Infrastructure.Caching;

public sealed class RedisProviderResponseCache(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> options) : IProviderResponseCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(options.Value.FlightApiOneWayTtlMinutes);

    public async Task<T?> GetAsync<T>(string cacheKey, CancellationToken cancellationToken)
    {
        var cachedValue = await _database.StringGetAsync(cacheKey);
        if (cachedValue.IsNullOrEmpty)
        {
            return default;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<T>(cachedValue.ToString(), SerializerOptions);
    }

    public async Task SetAsync<T>(string cacheKey, T response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.Serialize(response, SerializerOptions);
        await _database.StringSetAsync(cacheKey, payload, _ttl);
    }
}
