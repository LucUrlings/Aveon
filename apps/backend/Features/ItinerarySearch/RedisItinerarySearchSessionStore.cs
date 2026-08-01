using System.Text.Json;
using backend.Features.ItinerarySearch.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace backend.Features.ItinerarySearch;

public sealed class RedisItinerarySearchSessionStore(IConnectionMultiplexer connectionMultiplexer, IOptions<MultiDestinationSearchOptions> options) : IItinerarySearchSessionStore
{
    private const string SetUnlessCanceledScript = """
        local current = redis.call('GET', KEYS[1])
        if current then
            local session = cjson.decode(current)
            if session.Status == 'canceled' then
                return 0
            end
        end
        redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        return 1
        """;
    private readonly IDatabase _database = connectionMultiplexer.GetDatabase();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(options.Value.SessionTtlMinutes);
    public async Task SetAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _database.StringSetAsync($"itinerary-search:{session.SearchId}", JsonSerializer.Serialize(session), _ttl);
    }
    public async Task<bool> TrySetUnlessCanceledAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _database.ScriptEvaluateAsync(
            SetUnlessCanceledScript,
            [$"itinerary-search:{session.SearchId}"],
            [JsonSerializer.Serialize(session), (long)_ttl.TotalMilliseconds]);
        cancellationToken.ThrowIfCancellationRequested();
        return (long)result == 1;
    }
    public async Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken)
    {
        var value = await _database.StringGetAsync($"itinerary-search:{searchId}");
        cancellationToken.ThrowIfCancellationRequested();
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<ItinerarySearchSessionResponse>(value.ToString());
    }
}
