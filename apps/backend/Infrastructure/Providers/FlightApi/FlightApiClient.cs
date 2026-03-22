using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FlightApiOneWayResponse>(SerializerOptions, cancellationToken);
        var result = payload ?? new FlightApiOneWayResponse();

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
        var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FlightApiCodeLookupResponse>(SerializerOptions, cancellationToken);
        var result = payload ?? new FlightApiCodeLookupResponse();

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
