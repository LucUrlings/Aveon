using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class FlightApiClient(
    HttpClient httpClient,
    IOptions<FlightApiOptions> options,
    IProviderResponseCache providerResponseCache,
    ILogger<FlightApiClient> logger) : IFlightSearchProvider, IAirportLookupProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly FlightApiOptions _options = options.Value;

    public async Task<FlightApiOneWayResponse> SearchOneWayAsync(
        ProviderSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("FlightApi:ApiKey is not configured.");
        }

        var cacheKey = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(request);
        var cachedResponse = await providerResponseCache.GetAsync<FlightApiOneWayResponse>(cacheKey, cancellationToken);
        if (cachedResponse is not null)
        {
            logger.LogInformation("FlightApi cache hit for key {CacheKey}", cacheKey);
            return cachedResponse;
        }

        logger.LogInformation("FlightApi cache miss for key {CacheKey}", cacheKey);

        var cabinClass = MapCabinClass(request.CabinClass);
        var path = $"onewaytrip/{_options.ApiKey}/{request.OriginAirport}/{request.DestinationAirport}/{request.DepartureDate:yyyy-MM-dd}/{request.Adults}/0/0/{cabinClass}/{_options.Currency}";
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Calling FlightApi one-way search for {OriginAirport} -> {DestinationAirport} on {DepartureDate} for {Adults} adults in {CabinClass} using cache key {CacheKey}",
            request.OriginAirport,
            request.DestinationAirport,
            request.DepartureDate,
            request.Adults,
            cabinClass,
            cacheKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "FlightApi one-way search request failed for {OriginAirport} -> {DestinationAirport} on {DepartureDate} after {ElapsedMilliseconds} ms",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "FlightApi one-way search returned HTTP {StatusCode} for {OriginAirport} -> {DestinationAirport} on {DepartureDate} after {ElapsedMilliseconds} ms",
                (int)response.StatusCode,
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                stopwatch.ElapsedMilliseconds);
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FlightApiOneWayResponse>(SerializerOptions, cancellationToken);
        var result = payload ?? new FlightApiOneWayResponse();
        stopwatch.Stop();

        logger.LogInformation(
            "FlightApi one-way search succeeded for {OriginAirport} -> {DestinationAirport} on {DepartureDate} with HTTP {StatusCode} in {ElapsedMilliseconds} ms",
            request.OriginAirport,
            request.DestinationAirport,
            request.DepartureDate,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds);

        await providerResponseCache.SetAsync(cacheKey, result, cancellationToken);
        logger.LogInformation("Stored FlightApi response in cache for key {CacheKey}", cacheKey);

        return result;
    }

    public async Task<FlightApiCodeLookupResponse> SearchAirportsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("FlightApi:ApiKey is not configured.");
        }

        var cacheKey = ProviderCacheKeyBuilder.BuildFlightApiAirportLookupKey(query);
        var cachedResponse = await providerResponseCache.GetAsync<FlightApiCodeLookupResponse>(cacheKey, cancellationToken);
        if (cachedResponse is not null)
        {
            logger.LogInformation("FlightApi airport lookup cache hit for key {CacheKey}", cacheKey);
            return cachedResponse;
        }

        logger.LogInformation("FlightApi airport lookup cache miss for key {CacheKey}", cacheKey);

        var path = $"iata/{_options.ApiKey}?name={Uri.EscapeDataString(query)}&type=airport";
        var trimmedQuery = query.Trim();
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Calling FlightApi airport lookup for query {Query} using cache key {CacheKey}",
            trimmedQuery,
            cacheKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(path, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "FlightApi airport lookup request failed for query {Query} after {ElapsedMilliseconds} ms",
                trimmedQuery,
                stopwatch.ElapsedMilliseconds);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            stopwatch.Stop();
            logger.LogWarning(
                "FlightApi airport lookup returned HTTP {StatusCode} for query {Query} after {ElapsedMilliseconds} ms",
                (int)response.StatusCode,
                trimmedQuery,
                stopwatch.ElapsedMilliseconds);
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FlightApiCodeLookupResponse>(SerializerOptions, cancellationToken);
        var result = payload ?? new FlightApiCodeLookupResponse();
        stopwatch.Stop();

        logger.LogInformation(
            "FlightApi airport lookup succeeded for query {Query} with HTTP {StatusCode} in {ElapsedMilliseconds} ms",
            trimmedQuery,
            (int)response.StatusCode,
            stopwatch.ElapsedMilliseconds);

        await providerResponseCache.SetAsync(cacheKey, result, cancellationToken);
        logger.LogInformation("Stored FlightApi airport lookup response in cache for key {CacheKey}", cacheKey);

        return result;
    }

    private static string MapCabinClass(string cabinClass) =>
        cabinClass.Trim().ToLowerInvariant() switch
        {
            "economy" => "Economy",
            "business" => "Business",
            "first" => "First",
            "premiumeconomy" => "Premium_Economy",
            "premium_economy" => "Premium_Economy",
            "premium economy" => "Premium_Economy",
            _ => "Economy"
        };
}
