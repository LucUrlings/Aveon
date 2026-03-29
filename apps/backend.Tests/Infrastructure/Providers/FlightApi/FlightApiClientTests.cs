using System.Net;
using System.Text;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class FlightApiClientTests
{
    [Fact]
    public async Task SearchOneWayAsync_ReturnsCachedResponse_WithoutCallingHttp()
    {
        var cache = new RecordingProviderResponseCache
        {
            CachedFlightResponse = new FlightApiOneWayResponse
            {
                Itineraries = [new FlightApiItinerary { Id = "cached-itinerary" }]
            }
        };
        var handler = new RecordingHttpMessageHandler();
        var client = CreateClient(handler, cache);

        var response = await client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None);

        Assert.Equal("cached-itinerary", response.Itineraries[0].Id);
        Assert.Equal(0, handler.CallCount);
        Assert.Contains("provider:flightapi:oneway:DUB:AMS:2026-05-15:1:premium economy", cache.RequestedKeys);
    }

    [Fact]
    public async Task SearchOneWayAsync_CallsExpectedPath_AndStoresResponse()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new RecordingHttpMessageHandler(
            """{"itineraries":[{"id":"live-itinerary"}],"legs":[],"segments":[],"places":[],"carriers":[],"agents":[]}""");
        var client = CreateClient(handler, cache);

        var response = await client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None);

        Assert.Equal("live-itinerary", response.Itineraries[0].Id);
        Assert.Equal("https://api.flightapi.io/onewaytrip/test-key/DUB/AMS/2026-05-15/1/0/0/Premium_Economy/EUR", handler.LastRequestUri);
        Assert.Equal("provider:flightapi:oneway:DUB:AMS:2026-05-15:1:premium economy", cache.LastSetKey);
        Assert.NotNull(cache.LastFlightResponse);
    }

    [Fact]
    public async Task SearchAirportsAsync_CallsExpectedPath_AndStoresResponse()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new RecordingHttpMessageHandler("""{"data":[{"fs":"DUB","name":"Dublin"}]}""");
        var client = CreateClient(handler, cache);

        var response = await client.SearchAirportsAsync(" Dublin Airport ", CancellationToken.None);

        Assert.Equal("DUB", response.Data[0].Fs);
        Assert.Equal("https://api.flightapi.io/iata/test-key?name=%20Dublin%20Airport%20&type=airport", handler.LastRequestUri);
        Assert.Equal("provider:flightapi:airport-lookup:dublin airport", cache.LastSetKey);
        Assert.NotNull(cache.LastAirportLookupResponse);
    }

    [Fact]
    public async Task SearchOneWayAsync_Throws_WhenApiKeyMissing()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new RecordingHttpMessageHandler();
        var client = new FlightApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.flightapi.io/") },
            Options.Create(new FlightApiOptions()),
            cache,
            NullLogger<FlightApiClient>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None));

        Assert.Equal("FlightApi:ApiKey is not configured.", error.Message);
    }

    private static ProviderSearchRequest CreateSearchRequest() =>
        new("DUB", "AMS", new DateOnly(2026, 5, 15), 1, "premium economy");

    private static FlightApiClient CreateClient(
        HttpMessageHandler handler,
        IProviderResponseCache cache) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.flightapi.io/") },
            Options.Create(new FlightApiOptions
            {
                ApiKey = "test-key",
                Currency = "EUR"
            }),
            cache,
            NullLogger<FlightApiClient>.Instance);

    private sealed class RecordingProviderResponseCache : IProviderResponseCache
    {
        public List<string> RequestedKeys { get; } = [];

        public string? LastSetKey { get; private set; }

        public FlightApiOneWayResponse? CachedFlightResponse { get; init; }

        public FlightApiOneWayResponse? LastFlightResponse { get; private set; }

        public FlightApiCodeLookupResponse? LastAirportLookupResponse { get; private set; }

        public Task<T?> GetAsync<T>(string cacheKey, CancellationToken cancellationToken)
        {
            RequestedKeys.Add(cacheKey);

            object? value = typeof(T) == typeof(FlightApiOneWayResponse)
                ? CachedFlightResponse
                : null;

            return Task.FromResult((T?)value);
        }

        public Task SetAsync<T>(string cacheKey, T response, CancellationToken cancellationToken)
        {
            LastSetKey = cacheKey;

            if (response is FlightApiOneWayResponse flightResponse)
            {
                LastFlightResponse = flightResponse;
            }

            if (response is FlightApiCodeLookupResponse airportLookupResponse)
            {
                LastAirportLookupResponse = airportLookupResponse;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHttpMessageHandler(string content = "{}") : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount += 1;
            LastRequestUri = request.RequestUri?.OriginalString;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }
}
