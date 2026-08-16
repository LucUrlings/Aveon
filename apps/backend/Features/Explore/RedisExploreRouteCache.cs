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

    public async Task<ExploreScheduleCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await _database.StringGetAsync(BuildKey(origin, profile, departureDate));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ExploreScheduleCacheEntry>(value.ToString(), SerializerOptions);
    }

    public async Task SetAsync(string origin, ExploreCacheProfile profile, ExploreScheduleCacheEntry entry, CancellationToken cancellationToken, DateOnly? departureDate = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retentionMinutes = RetentionMinutes(profile, _options);
        var payload = JsonSerializer.Serialize(entry, SerializerOptions);
        await _database.StringSetAsync(BuildKey(origin, profile, departureDate), payload, TimeSpan.FromMinutes(retentionMinutes));
    }

    internal static string BuildKey(string origin, ExploreCacheProfile profile, DateOnly? departureDate) =>
        $"explore:routes:v2:{profile.ToString().ToLowerInvariant()}:{origin.Trim().ToUpperInvariant()}:{departureDate?.ToString("yyyy-MM-dd") ?? "rolling"}";

    internal static int RetentionMinutes(ExploreCacheProfile profile, RedisOptions options) => Math.Max(
        profile == ExploreCacheProfile.Hero ? options.HeroRoutesRetentionMinutes : options.ExploreRoutesRetentionMinutes,
        1);
}
