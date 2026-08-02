using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class FlightApiClient(
    HttpClient httpClient,
    IOptions<FlightApiOptions> options,
    IProviderResponseCache providerResponseCache,
    IFlightApiRequestGate requestGate,
    IProviderRequestCoalescer requestCoalescer,
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
            requestGate.RecordCacheHit();
            logger.LogInformation("FlightApi cache hit for key {CacheKey}", cacheKey);
            return cachedResponse;
        }

        logger.LogInformation("FlightApi cache miss for key {CacheKey}", cacheKey);

        var cabinClass = MapCabinClass(request.CabinClass);
        var path = $"onewaytrip/{_options.ApiKey}/{request.OriginAirport}/{request.DestinationAirport}/{request.DepartureDate:yyyy-MM-dd}/{request.Adults}/0/0/{cabinClass}/{request.Currency}";
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Calling FlightApi one-way search for {OriginAirport} -> {DestinationAirport} on {DepartureDate} for {Adults} adults in {CabinClass} using cache key {CacheKey}",
            request.OriginAirport,
            request.DestinationAirport,
            request.DepartureDate,
            request.Adults,
            cabinClass,
            cacheKey);

        try
        {
            var result = await requestCoalescer.RunAsync(cacheKey, async () =>
            {
                var live = await GetLiveResponseAsync<FlightApiOneWayResponse>(path, cancellationToken);
                await providerResponseCache.SetAsync(cacheKey, live, cancellationToken);
                return live;
            }, cancellationToken);
            stopwatch.Stop();
            logger.LogInformation(
                "FlightApi one-way search succeeded for {OriginAirport} -> {DestinationAirport} on {DepartureDate} in {ElapsedMilliseconds} ms",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                stopwatch.ElapsedMilliseconds);

            return result;
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
    }

    public async Task<FlightApiOneWayResponse> SearchRoundTripAsync(
        ProviderRoundTripSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("FlightApi:ApiKey is not configured.");
        }

        var cacheKey = ProviderCacheKeyBuilder.BuildFlightApiRoundTripSearchKey(request);
        var cachedResponse = await providerResponseCache.GetAsync<FlightApiOneWayResponse>(cacheKey, cancellationToken);
        if (cachedResponse is not null)
        {
            requestGate.RecordCacheHit();
            logger.LogInformation("FlightApi round-trip cache hit for key {CacheKey}", cacheKey);
            return cachedResponse;
        }

        logger.LogInformation("FlightApi round-trip cache miss for key {CacheKey}", cacheKey);

        var cabinClass = MapCabinClass(request.CabinClass);
        var path = $"roundtrip/{_options.ApiKey}/{request.OriginAirport}/{request.DestinationAirport}/{request.DepartureDate:yyyy-MM-dd}/{request.ReturnDate:yyyy-MM-dd}/{request.Adults}/0/0/{cabinClass}/{request.Currency}";
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation(
            "Calling FlightApi round-trip search for {OriginAirport} -> {DestinationAirport} on {DepartureDate} returning {ReturnDate} for {Adults} adults in {CabinClass} using cache key {CacheKey}",
            request.OriginAirport,
            request.DestinationAirport,
            request.DepartureDate,
            request.ReturnDate,
            request.Adults,
            cabinClass,
            cacheKey);

        try
        {
            var result = await requestCoalescer.RunAsync(cacheKey, async () =>
            {
                var live = await GetLiveResponseAsync<FlightApiOneWayResponse>(path, cancellationToken);
                await providerResponseCache.SetAsync(cacheKey, live, cancellationToken);
                return live;
            }, cancellationToken);
            stopwatch.Stop();
            logger.LogInformation(
                "FlightApi round-trip search succeeded for {OriginAirport} -> {DestinationAirport} on {DepartureDate} returning {ReturnDate} in {ElapsedMilliseconds} ms",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                request.ReturnDate,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "FlightApi round-trip search request failed for {OriginAirport} -> {DestinationAirport} on {DepartureDate} returning {ReturnDate} after {ElapsedMilliseconds} ms",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                request.ReturnDate,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
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
            requestGate.RecordCacheHit();
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

        try
        {
            var result = await requestCoalescer.RunAsync(cacheKey, async () =>
            {
                var live = await GetLiveResponseAsync<FlightApiCodeLookupResponse>(path, cancellationToken);
                await providerResponseCache.SetAsync(cacheKey, live, cancellationToken);
                return live;
            }, cancellationToken);
            stopwatch.Stop();
            logger.LogInformation(
                "FlightApi airport lookup succeeded for query {Query} in {ElapsedMilliseconds} ms",
                trimmedQuery,
                stopwatch.ElapsedMilliseconds);

            return result;
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
    }

    private async Task<T> GetLiveResponseAsync<T>(string path, CancellationToken cancellationToken)
        where T : new()
    {
        var maximumAttempts = Math.Max(_options.MaxRetryAttempts, 1);

        for (var attempt = 1; attempt <= maximumAttempts; attempt += 1)
        {
            TimeSpan? retryDelay = null;
            using (await requestGate.AcquireAsync(cancellationToken))
            {
                requestGate.RecordLiveCall();
                using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    requestGate.RecordThrottledResponse();
                    if (attempt < maximumAttempts)
                    {
                        retryDelay = GetRetryDelay(response, attempt, _options.MaxRetryDelaySeconds);
                    }
                }

                if (retryDelay is null)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = await ReadErrorSummaryAsync(response, cancellationToken);
                        var providerRequestId = ProviderRequestId(response);
                        throw new FlightApiResponseException(response.StatusCode, summary, providerRequestId);
                    }
                    return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken) ?? new T();
                }
            }

            logger.LogWarning("FlightApi returned HTTP 429; retrying attempt {Attempt} of {MaximumAttempts} after {Delay}", attempt + 1, maximumAttempts, retryDelay);
            await Task.Delay(retryDelay.Value, cancellationToken);
        }

        throw new InvalidOperationException("FlightApi retry loop completed without a response.");
    }

    private async Task<string> ReadErrorSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maximumCharacters = 2048;
        if (response.Content is null) return "No response body";

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var buffer = new char[maximumCharacters + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
        var raw = new string(buffer, 0, Math.Min(read, maximumCharacters));
        var diagnostic = ExtractDiagnostic(raw);
        return SanitizeDiagnostic(diagnostic, read > maximumCharacters);
    }

    private static string ExtractDiagnostic(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Empty response body";
        try
        {
            using var document = JsonDocument.Parse(raw);
            return FindDiagnostic(document.RootElement, 0) ?? "Provider returned an error without a message";
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string? FindDiagnostic(JsonElement value, int depth)
    {
        if (depth > 4) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var found = FindDiagnostic(item, depth + 1);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object) return null;

        var preferredNames = new[] { "message", "error", "detail", "title", "reason" };
        foreach (var preferredName in preferredNames)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!string.Equals(property.Name, preferredName, StringComparison.OrdinalIgnoreCase)) continue;
                var found = FindDiagnostic(property.Value, depth + 1);
                if (!string.IsNullOrWhiteSpace(found)) return found;
            }
        }
        return null;
    }

    private string SanitizeDiagnostic(string? value, bool truncated)
    {
        var sanitized = string.IsNullOrWhiteSpace(value) ? "Provider returned an error without a message" : value;
        if (!string.IsNullOrWhiteSpace(_options.ApiKey)) sanitized = sanitized.Replace(_options.ApiKey, "[redacted]", StringComparison.Ordinal);
        sanitized = Regex.Replace(
            sanitized,
            "(?i)(api[_-]?key|token|secret|password|authorization)\\s*[:=]\\s*[^\\s,;&]+",
            "$1=[redacted]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
        sanitized = Regex.Replace(sanitized, "\\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)).Trim();
        if (sanitized.Length > 512) sanitized = sanitized[..512];
        return truncated ? $"{sanitized}… [truncated]" : sanitized;
    }

    private static string? ProviderRequestId(HttpResponseMessage response)
    {
        var headerNames = new[] { "x-request-id", "request-id", "x-correlation-id", "cf-ray", "traceparent" };
        foreach (var headerName in headerNames)
        {
            if (!response.Headers.TryGetValues(headerName, out var values) &&
                (response.Content is null || !response.Content.Headers.TryGetValues(headerName, out values))) continue;
            var value = values.FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            var safe = new string(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':').Take(128).ToArray());
            if (!string.IsNullOrWhiteSpace(safe)) return safe;
        }
        return null;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt, int maxRetryDelaySeconds)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
        {
            retryAfter = retryDate - DateTimeOffset.UtcNow;
        }
        if (retryAfter is not null)
        {
            return ClampDelay(retryAfter.Value > TimeSpan.Zero ? retryAfter.Value : TimeSpan.FromMilliseconds(100), maxRetryDelaySeconds);
        }

        var boundedExponentialDelay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 151));
        return ClampDelay(boundedExponentialDelay + jitter, maxRetryDelaySeconds);
    }

    private static TimeSpan ClampDelay(TimeSpan delay, int maxRetryDelaySeconds) =>
        delay <= TimeSpan.FromSeconds(Math.Max(maxRetryDelaySeconds, 1))
            ? delay
            : TimeSpan.FromSeconds(Math.Max(maxRetryDelaySeconds, 1));

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
