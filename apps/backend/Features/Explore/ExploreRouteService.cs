using backend.Features.Explore.Models;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Options;

namespace backend.Features.Explore;

public sealed class ExploreRouteService(
    IAirportScheduleProvider scheduleProvider,
    IExploreRouteCache cache,
    IProviderRequestCoalescer requestCoalescer,
    IOptions<RedisOptions> redisOptions,
    IOptions<FlightApiOptions> flightApiOptions,
    TimeProvider timeProvider,
    ILogger<ExploreRouteService> logger) : IExploreRouteService
{
    // FlightAPI Schedule v1 documents its no-day response as the previous five days through the next five days.
    private const int ScheduleWindowRadiusDays = 5;
    internal static readonly string[] HeroHubs = ["DUB", "AMS", "LHR", "CDG", "FRA", "JFK", "ATL", "DXB", "DOH", "SIN", "HND", "SYD"];
    private readonly RedisOptions _redisOptions = redisOptions.Value;
    private readonly FlightApiOptions _flightApiOptions = flightApiOptions.Value;

    public async Task<ExploreRoutesResponse> GetRoutesAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken)
    {
        var normalized = origin.Trim().ToUpperInvariant();
        var now = timeProvider.GetUtcNow();
        var cached = await cache.GetAsync(normalized, profile, cancellationToken);
        if (cached is not null && now - cached.FetchedAt <= Freshness(profile))
        {
            logger.LogInformation(
                "Explore route cache hit for {Origin} using {CacheProfile}; no live FlightAPI schedule requests required",
                normalized,
                profile);
            return cached.Response with { IsStale = false };
        }

        if (cached is null)
        {
            logger.LogInformation(
                "Explore route cache miss for {Origin} using {CacheProfile}; refreshing from live FlightAPI schedules",
                normalized,
                profile);
        }
        else
        {
            logger.LogInformation(
                "Explore route cache is stale for {Origin} using {CacheProfile}; refreshing from live FlightAPI schedules",
                normalized,
                profile);
        }

        try
        {
            var refreshed = await requestCoalescer.RunAsync(
                $"explore:schedule-refresh:{normalized}",
                () => FetchRoutesAsync(normalized, cancellationToken),
                cancellationToken);
            if (!refreshed.IsComplete && cached is not null)
            {
                logger.LogWarning("FlightApi returned a partial schedule refresh for {Origin}; serving the retained complete network", normalized);
                return cached.Response with { IsStale = true };
            }
            if (refreshed.IsComplete)
            {
                await cache.SetAsync(normalized, profile, new(refreshed, refreshed.FetchedAt), cancellationToken);
            }
            return refreshed;
        }
        catch (Exception exception) when (cached is not null && exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "FlightApi schedule refresh failed for {Origin}; serving stale route network", normalized);
            return cached.Response with { IsStale = true };
        }
    }

    public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken)
    {
        var origin = SelectHeroHub(Random.Shared);
        return GetRoutesAsync(origin, ExploreCacheProfile.Hero, cancellationToken);
    }

    internal static string SelectHeroHub(Random random) => HeroHubs[random.Next(HeroHubs.Length)];

    private async Task<ExploreRoutesResponse> FetchRoutesAsync(string origin, CancellationToken cancellationToken)
    {
        var first = await scheduleProvider.SearchDepartureScheduleAsync(origin, 1, cancellationToken);
        var reportedPages = Math.Max(first.Airport?.PluginData?.Schedule?.Departures?.Page?.Total ?? 1, 1);
        var pageLimit = Math.Max(_flightApiOptions.MaxSchedulePages, 1);
        var pagesToFetch = Math.Min(reportedPages, pageLimit);
        var remaining = Enumerable.Range(2, Math.Max(pagesToFetch - 1, 0))
            .Select(FetchRemainingPageAsync);
        var responses = new List<FlightApiScheduleResponse> { first };
        var remainingResponses = await Task.WhenAll(remaining);
        responses.AddRange(remainingResponses.Where(response => response is not null).Cast<FlightApiScheduleResponse>());

        var details = first.Airport?.PluginData?.Details
            ?? throw new InvalidOperationException($"FlightApi schedule response did not include details for {origin}.");
        var originAirport = MapAirport(details.Code?.Iata ?? origin, details.Name, details.Position)
            ?? throw new InvalidOperationException($"FlightApi schedule response did not include valid coordinates for {origin}.");
        var destinations = responses
            .SelectMany(response => response.Airport?.PluginData?.Schedule?.Departures?.Data ?? [])
            .Select(row => row.Flight?.Airport?.Destination)
            .Where(destination => destination is not null)
            .Select(destination => MapAirport(destination!.Code?.Iata, destination.Name, destination.Position))
            .Where(destination => destination is not null && !string.Equals(destination.Code, originAirport.Code, StringComparison.OrdinalIgnoreCase))
            .Cast<ExploreAirport>()
            .DistinctBy(destination => destination.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(destination => destination.City, StringComparer.OrdinalIgnoreCase)
            .ThenBy(destination => destination.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fetchedAt = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(fetchedAt.UtcDateTime);
        return new(
            originAirport,
            destinations,
            today.AddDays(-ScheduleWindowRadiusDays),
            today.AddDays(ScheduleWindowRadiusDays),
            fetchedAt,
            reportedPages <= pageLimit && remainingResponses.All(response => response is not null),
            false);

        async Task<FlightApiScheduleResponse?> FetchRemainingPageAsync(int page)
        {
            try
            {
                return await scheduleProvider.SearchDepartureScheduleAsync(origin, page, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "FlightApi schedule page {Page} failed for {Origin}; returning an explicitly partial network", page, origin);
                return null;
            }
        }
    }

    private TimeSpan Freshness(ExploreCacheProfile profile) => TimeSpan.FromMinutes(Math.Max(
        profile == ExploreCacheProfile.Hero ? _redisOptions.HeroRoutesTtlMinutes : _redisOptions.ExploreRoutesTtlMinutes,
        1));

    private static ExploreAirport? MapAirport(string? code, string? name, FlightApiSchedulePosition? position)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode.Length != 3 || position is null || position.Latitude is < -90 or > 90 || position.Longitude is < -180 or > 180 || (position.Latitude == 0 && position.Longitude == 0)) return null;
        var city = position.Region?.City?.Trim();
        var airportName = name?.Trim();
        return new(
            normalizedCode,
            string.IsNullOrWhiteSpace(airportName) ? normalizedCode : airportName,
            string.IsNullOrWhiteSpace(city) ? normalizedCode : city,
            position.Country?.Name?.Trim() ?? string.Empty,
            position.Latitude,
            position.Longitude);
    }
}
