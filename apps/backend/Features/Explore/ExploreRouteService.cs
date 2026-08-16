using backend.Features.Explore.Models;
using backend.Infrastructure.Airports;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace backend.Features.Explore;

public sealed class ExploreRouteService(
    IAirportScheduleProvider scheduleProvider,
    IAirportCatalogRepository airportCatalog,
    IExploreRouteCache cache,
    IProviderRequestCoalescer requestCoalescer,
    IHeroRouteWarmupQueue heroWarmupQueue,
    IOptions<RedisOptions> redisOptions,
    IOptions<FlightApiOptions> flightApiOptions,
    TimeProvider timeProvider,
    ILogger<ExploreRouteService> logger) : IExploreRouteService
{
    private const int ScheduleWindowRadiusDays = 5;
    internal static readonly string[] HeroHubs = ["DUB", "AMS", "LHR", "CDG", "FRA", "JFK", "ATL", "DXB", "DOH", "SIN", "HND", "SYD"];
    private readonly RedisOptions _redisOptions = redisOptions.Value;
    private readonly FlightApiOptions _flightApiOptions = flightApiOptions.Value;

    public async Task<ExploreRoutesResponse> GetRoutesAsync(
        string origin,
        ExploreCacheProfile profile,
        CancellationToken cancellationToken,
        DateOnly? departureDate = null)
    {
        var normalized = origin.Trim().ToUpperInvariant();
        var now = timeProvider.GetUtcNow();
        var cached = await cache.GetAsync(normalized, profile, cancellationToken, departureDate);
        if (cached is not null && now - cached.FetchedAt <= Freshness(profile))
        {
            logger.LogInformation(
                "Explore route cache hit for {Origin} using {CacheProfile} on {DepartureDate}; no live FlightAPI schedule requests required",
                normalized,
                profile,
                departureDate);
            return await EnrichAsync(cached, isStale: false, cancellationToken);
        }

        logger.LogInformation(
            cached is null
                ? "Explore route cache miss for {Origin} using {CacheProfile} on {DepartureDate}; refreshing from live FlightAPI schedules"
                : "Explore route cache is stale for {Origin} using {CacheProfile} on {DepartureDate}; refreshing from live FlightAPI schedules",
            normalized,
            profile,
            departureDate);

        try
        {
            var refreshed = await requestCoalescer.RunAsync(
                $"explore:schedule-refresh:{normalized}:{departureDate?.ToString("yyyy-MM-dd") ?? "rolling"}",
                sharedCancellationToken => departureDate.HasValue
                    ? FetchDatedScheduleAsync(normalized, departureDate.Value, sharedCancellationToken)
                    : FetchRollingScheduleAsync(normalized, sharedCancellationToken),
                cancellationToken);
            if (!refreshed.IsComplete && cached is not null)
            {
                logger.LogWarning("FlightApi returned a partial schedule refresh for {Origin}; serving the retained complete network", normalized);
                return await EnrichAsync(cached, isStale: true, cancellationToken);
            }
            if (refreshed.IsComplete)
                await cache.SetAsync(normalized, profile, refreshed, cancellationToken, departureDate);
            return await EnrichAsync(refreshed, isStale: false, cancellationToken);
        }
        catch (Exception exception) when (cached is not null && IsProviderFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "FlightApi schedule refresh failed for {Origin}; serving stale route network", normalized);
            return await EnrichAsync(cached, isStale: true, cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "FlightApi could not provide the required first schedule page for {Origin}", normalized);
            throw new ExploreProviderUnavailableException("FlightAPI could not provide the required schedule page.", exception);
        }
    }

    public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken) =>
        GetHeroRoutesAsync(SelectHeroHub(Random.Shared), cancellationToken);

    internal async Task<ExploreRoutesResponse> GetHeroRoutesAsync(string preferredHub, CancellationToken cancellationToken)
    {
        var normalizedPreferredHub = preferredHub.Trim().ToUpperInvariant();
        var now = timeProvider.GetUtcNow();
        var cachedCandidates = await Task.WhenAll(HeroHubs.Select(async hub => new
        {
            Hub = hub,
            Entry = await cache.GetAsync(hub, ExploreCacheProfile.Hero, cancellationToken)
        }));
        var retained = cachedCandidates
            .Where(candidate => candidate.Entry is not null
                && string.Equals(candidate.Entry.OriginCode, candidate.Hub, StringComparison.Ordinal))
            .ToArray();
        var fresh = retained
            .Where(candidate => now - candidate.Entry!.FetchedAt <= Freshness(ExploreCacheProfile.Hero))
            .ToArray();

        if (fresh.Length > 0)
        {
            var selected = fresh.FirstOrDefault(candidate => candidate.Hub == normalizedPreferredHub)
                ?? fresh[Random.Shared.Next(fresh.Length)];
            if (selected.Hub != normalizedPreferredHub) QueueHeroWarmup(normalizedPreferredHub);
            logger.LogInformation("Serving cached homepage route preview for {SelectedOrigin}; preferred random hub was {PreferredOrigin}", selected.Hub, normalizedPreferredHub);
            return await EnrichAsync(selected.Entry!, isStale: false, cancellationToken);
        }

        if (retained.Length > 0)
        {
            var selected = retained.FirstOrDefault(candidate => candidate.Hub == normalizedPreferredHub)
                ?? retained[Random.Shared.Next(retained.Length)];
            QueueHeroWarmup(normalizedPreferredHub);
            logger.LogInformation("Serving retained homepage route preview for {SelectedOrigin} while {PreferredOrigin} warms in the background", selected.Hub, normalizedPreferredHub);
            return await EnrichAsync(selected.Entry!, isStale: true, cancellationToken);
        }

        ExploreScheduleCacheEntry preview;
        try
        {
            preview = await requestCoalescer.RunAsync(
                "explore:hero-initial-preview",
                sharedCancellationToken => FetchRollingSchedulePreviewAsync(normalizedPreferredHub, sharedCancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            logger.LogError(exception, "FlightAPI could not provide the initial homepage route preview for {Origin}", normalizedPreferredHub);
            throw new ExploreProviderUnavailableException("FlightAPI could not provide the initial homepage route preview.", exception);
        }
        if (preview.IsComplete)
            await cache.SetAsync(preview.OriginCode, ExploreCacheProfile.Hero, preview, cancellationToken);
        else
            QueueHeroWarmup(preview.OriginCode);
        logger.LogInformation("Serving first-page homepage route preview for {Origin}; complete network queued for background warming: {RequiresWarmup}", preview.OriginCode, !preview.IsComplete);
        return await EnrichAsync(preview, isStale: false, cancellationToken);
    }

    internal static string SelectHeroHub(Random random) => HeroHubs[random.Next(HeroHubs.Length)];

    private void QueueHeroWarmup(string origin)
    {
        if (heroWarmupQueue.TryEnqueue(origin))
            logger.LogInformation("Queued background homepage route warmup for {Origin}", origin);
    }

    private async Task<ExploreScheduleCacheEntry> FetchRollingSchedulePreviewAsync(string origin, CancellationToken cancellationToken)
    {
        var first = await scheduleProvider.SearchDepartureScheduleAsync(origin, 1, cancellationToken);
        ValidateRollingSchedulePage(first, origin, 1);
        var reportedPages = Math.Max(first.Airport?.PluginData?.Schedule?.Departures?.Page?.Total ?? 1, 1);
        return BuildRollingSchedule(origin, [first], reportedPages == 1);
    }

    private async Task<ExploreScheduleCacheEntry> FetchRollingScheduleAsync(string origin, CancellationToken cancellationToken)
    {
        var first = await scheduleProvider.SearchDepartureScheduleAsync(origin, 1, cancellationToken);
        ValidateRollingSchedulePage(first, origin, 1);
        var reportedPages = Math.Max(first.Airport?.PluginData?.Schedule?.Departures?.Page?.Total ?? 1, 1);
        var pageLimit = Math.Max(_flightApiOptions.MaxSchedulePages, 1);
        var pagesToFetch = Math.Min(reportedPages, pageLimit);
        var remainingResponses = await Task.WhenAll(
            Enumerable.Range(2, Math.Max(pagesToFetch - 1, 0)).Select(FetchRemainingPageAsync));
        var responses = new List<FlightApiScheduleResponse> { first };
        responses.AddRange(remainingResponses.Where(response => response is not null).Cast<FlightApiScheduleResponse>());

        return BuildRollingSchedule(
            origin,
            responses,
            reportedPages <= pageLimit && remainingResponses.All(response => response is not null));

        async Task<FlightApiScheduleResponse?> FetchRemainingPageAsync(int page)
        {
            try
            {
                var response = await scheduleProvider.SearchDepartureScheduleAsync(origin, page, cancellationToken);
                ValidateRollingSchedulePage(response, origin, page);
                return response;
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                logger.LogWarning(exception, "FlightApi schedule page {Page} failed for {Origin}; returning an explicitly partial network", page, origin);
                return null;
            }
        }
    }

    private ExploreScheduleCacheEntry BuildRollingSchedule(
        string origin,
        IEnumerable<FlightApiScheduleResponse> responses,
        bool isComplete)
    {
        var destinationCodes = responses
            .SelectMany(response => response.Airport?.PluginData?.Schedule?.Departures?.Data ?? [])
            .Select(row => NormalizeCode(row.Flight?.Airport?.Destination?.Code?.Iata))
            .Where(code => code is not null && !string.Equals(code, origin, StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var fetchedAt = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(fetchedAt.UtcDateTime);
        return new(
            origin,
            destinationCodes,
            today.AddDays(-ScheduleWindowRadiusDays),
            today.AddDays(ScheduleWindowRadiusDays),
            fetchedAt,
            isComplete);
    }

    private async Task<ExploreScheduleCacheEntry> FetchDatedScheduleAsync(
        string origin,
        DateOnly departureDate,
        CancellationToken cancellationToken)
    {
        var pageLimit = Math.Min(Math.Max(_flightApiOptions.MaxSchedulePages, 1), 4);
        var first = await scheduleProvider.SearchDepartureScheduleV2Async(origin, departureDate, 1, cancellationToken);
        ValidateDatedSchedulePage(first, origin, requireOrigin: true);
        var remainingResponses = await Task.WhenAll(
            Enumerable.Range(2, Math.Max(pageLimit - 1, 0)).Select(FetchRemainingPageAsync));
        var responses = new List<FlightApiScheduleV2Response> { first };
        responses.AddRange(remainingResponses.Where(response => response is not null).Cast<FlightApiScheduleV2Response>());
        var destinationCodes = responses
            .SelectMany(response => response.Data?.Flights ?? [])
            .Select(flight => NormalizeCode(flight.Airport?.Fs ?? flight.Airport?.Iata))
            .Where(code => code is not null && !string.Equals(code, origin, StringComparison.Ordinal))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new(
            origin,
            destinationCodes,
            departureDate,
            departureDate,
            timeProvider.GetUtcNow(),
            pageLimit == 4 && remainingResponses.All(response => response is not null));

        async Task<FlightApiScheduleV2Response?> FetchRemainingPageAsync(int page)
        {
            try
            {
                var response = await scheduleProvider.SearchDepartureScheduleV2Async(origin, departureDate, page, cancellationToken);
                ValidateDatedSchedulePage(response, origin, requireOrigin: false);
                return response;
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
            {
                logger.LogWarning(exception, "FlightApi v2 schedule page {Page} failed for {Origin} on {DepartureDate}; returning an explicitly partial network", page, origin, departureDate);
                return null;
            }
        }
    }

    private async Task<ExploreRoutesResponse> EnrichAsync(
        ExploreScheduleCacheEntry schedule,
        bool isStale,
        CancellationToken cancellationToken)
    {
        var requestedCodes = schedule.DestinationCodes.Append(schedule.OriginCode).Distinct(StringComparer.Ordinal).ToArray();
        var airports = await airportCatalog.GetByIataCodesAsync(requestedCodes, cancellationToken);
        if (!airports.TryGetValue(schedule.OriginCode, out var origin))
        {
            logger.LogError("Airport catalogue does not contain Explore origin {Origin}", schedule.OriginCode);
            throw new AirportCatalogUnavailableException($"Airport catalogue does not contain origin {schedule.OriginCode}.");
        }

        var missingCodes = schedule.DestinationCodes.Where(code => !airports.ContainsKey(code)).ToArray();
        if (missingCodes.Length > 0)
        {
            logger.LogWarning(
                "Airport catalogue is missing {MissingAirportCount} scheduled destinations for {Origin}: {MissingAirportCodes}",
                missingCodes.Length,
                schedule.OriginCode,
                string.Join(",", missingCodes));
        }

        var destinations = schedule.DestinationCodes
            .Where(airports.ContainsKey)
            .Select(code => MapAirport(airports[code]))
            .OrderBy(airport => airport.City, StringComparer.OrdinalIgnoreCase)
            .ThenBy(airport => airport.Code, StringComparer.Ordinal)
            .ToList();
        return new(
            MapAirport(origin),
            destinations,
            schedule.ObservedFrom,
            schedule.ObservedTo,
            schedule.FetchedAt,
            schedule.IsComplete && missingCodes.Length == 0,
            isStale);
    }

    private TimeSpan Freshness(ExploreCacheProfile profile) => TimeSpan.FromMinutes(Math.Max(
        profile == ExploreCacheProfile.Hero ? _redisOptions.HeroRoutesTtlMinutes : _redisOptions.ExploreRoutesTtlMinutes,
        1));

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is FlightApiResponseException
            or ExploreProviderPayloadException
            or HttpRequestException
            or JsonException
            or TimeoutException
        || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static string? NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        return normalized is { Length: 3 } && normalized.All(character => character is >= 'A' and <= 'Z') ? normalized : null;
    }

    private static void ValidateRollingSchedulePage(
        FlightApiScheduleResponse response,
        string expectedOrigin,
        int expectedPage)
    {
        var pluginData = response.Airport?.PluginData;
        var departures = pluginData?.Schedule?.Departures;
        var page = departures?.Page;
        var returnedOrigin = NormalizeCode(pluginData?.Details?.Code?.Iata);
        if (departures?.Data is null
            || page is null
            || page.Current != expectedPage
            || Math.Max(page.Total, 1) < expectedPage
            || !string.Equals(returnedOrigin, expectedOrigin, StringComparison.Ordinal))
        {
            throw new ExploreProviderPayloadException(
                $"FlightAPI schedule page {expectedPage} did not contain the expected {expectedOrigin} departure schedule envelope.");
        }
    }

    private static void ValidateDatedSchedulePage(
        FlightApiScheduleV2Response response,
        string expectedOrigin,
        bool requireOrigin)
    {
        var returnedOrigin = NormalizeCode(
            response.Data?.Header?.DepartureAirport?.Fs
            ?? response.Data?.Header?.DepartureAirport?.Iata);
        if (response.Data?.Flights is null
            || requireOrigin && returnedOrigin is null
            || returnedOrigin is not null && !string.Equals(returnedOrigin, expectedOrigin, StringComparison.Ordinal))
        {
            throw new ExploreProviderPayloadException(
                $"FlightAPI exact-date schedule did not contain the expected {expectedOrigin} departure schedule envelope.");
        }
    }

    private static ExploreAirport MapAirport(AirportCatalogEntry airport) => new(
        airport.Iata,
        airport.Name,
        airport.City,
        CountryName(airport.CountryCode),
        airport.Latitude,
        airport.Longitude);

    private static string CountryName(string countryCode)
    {
        try
        {
            return new RegionInfo(countryCode).EnglishName;
        }
        catch (ArgumentException)
        {
            return countryCode;
        }
    }
}

public sealed class AirportCatalogUnavailableException(string message) : Exception(message);

public sealed class ExploreProviderUnavailableException(string message, Exception innerException) : Exception(message, innerException);

internal sealed class ExploreProviderPayloadException(string message) : Exception(message);
