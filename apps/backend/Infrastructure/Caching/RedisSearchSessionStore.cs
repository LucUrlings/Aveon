using System.Text.Json;
using backend.Features.Search.Models;
using backend.Infrastructure.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace backend.Infrastructure.Caching;

public sealed class RedisSearchSessionStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<RedisOptions> options) : ISearchSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(options.Value.SearchSessionTtlMinutes);

    public async Task<SearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken)
    {
        var cachedValue = await _database.StringGetAsync(BuildKey(searchId));
        if (cachedValue.IsNullOrEmpty)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Deserialize<SearchSessionResponse>(cachedValue.ToString(), SerializerOptions);
    }

    public async Task SetAsync(SearchSessionResponse session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.Serialize(session, SerializerOptions);
        await _database.StringSetAsync(BuildKey(session.SearchId), payload, _ttl);
    }

    private static string BuildKey(string searchId) => $"search-session:{searchId}";
}
