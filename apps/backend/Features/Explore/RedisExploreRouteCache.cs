using backend.Features.Explore.Models;
using backend.Infrastructure.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace backend.Features.Explore;

public sealed class RedisExploreRouteCache(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options) : IExploreRouteCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDatabase _database = redis.GetDatabase();
    private readonly RedisOptions _options = options.Value;

    public async Task<ExploreRouteCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _database.StringGetAsync(Key(origin, profile));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ExploreRouteCacheEntry>(value.ToString(), SerializerOptions);
    }

    public async Task SetAsync(string origin, ExploreCacheProfile profile, ExploreRouteCacheEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retentionMinutes = RetentionMinutes(profile, _options);
        var payload = JsonSerializer.Serialize(entry, SerializerOptions);
        await _database.StringSetAsync(Key(origin, profile), payload, TimeSpan.FromMinutes(retentionMinutes));
    }

    private static string Key(string origin, ExploreCacheProfile profile) =>
        $"explore:routes:{profile.ToString().ToLowerInvariant()}:{origin.Trim().ToUpperInvariant()}";

    internal static int RetentionMinutes(ExploreCacheProfile profile, RedisOptions options) => Math.Max(
        profile == ExploreCacheProfile.Hero ? options.HeroRoutesRetentionMinutes : options.ExploreRoutesRetentionMinutes,
        1);
}
