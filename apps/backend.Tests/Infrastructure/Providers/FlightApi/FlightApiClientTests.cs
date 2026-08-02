using System.Net;
using System.Text;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Logging;
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
        var gate = new RecordingGate();
        var client = CreateClient(handler, cache, gate);

        var response = await client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None);

        Assert.Equal("cached-itinerary", response.Itineraries[0].Id);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(0, gate.Acquisitions);
        Assert.Equal(1, gate.CacheHits);
        Assert.Contains("provider:flightapi:oneway:DUB:AMS:2026-05-15:1:premium economy:EUR", cache.RequestedKeys);
    }

    [Fact]
    public async Task SearchOneWayAsync_CallsExpectedPath_AndStoresResponse()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new RecordingHttpMessageHandler(
            """{"itineraries":[{"id":"live-itinerary"}],"legs":[],"segments":[],"places":[],"carriers":[],"agents":[]}""");
        var gate = new RecordingGate();
        var client = CreateClient(handler, cache, gate);

        var response = await client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None);

        Assert.Equal("live-itinerary", response.Itineraries[0].Id);
        Assert.Equal("https://api.flightapi.io/onewaytrip/test-key/DUB/AMS/2026-05-15/1/0/0/Premium_Economy/EUR", handler.LastRequestUri);
        Assert.Equal("provider:flightapi:oneway:DUB:AMS:2026-05-15:1:premium economy:EUR", cache.LastSetKey);
        Assert.NotNull(cache.LastFlightResponse);
        Assert.Equal((1, 1), (gate.Acquisitions, gate.Releases));
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
            CreateGate(),
            new ProviderRequestCoalescer(),
            NullLogger<FlightApiClient>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None));

        Assert.Equal("FlightApi:ApiKey is not configured.", error.Message);
    }

    [Fact]
    public async Task LiveSimpleOrderedOptimizedAndAirportRequests_ShareTheSameFivePermitGate()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new ConcurrentTrackingHttpMessageHandler(
            """{"itineraries":[],"legs":[],"segments":[],"places":[],"carriers":[],"agents":[]}""");
        using var gate = new FlightApiRequestGate(Options.Create(new FlightApiOptions { MaxConcurrentRequests = 5 }));
        var client = CreateClient(handler, cache, gate);

        var orderedEdges = Enumerable.Range(0, 4).Select(offset =>
            client.SearchOneWayAsync(CreateSearchRequest() with { DepartureDate = new DateOnly(2026, 5, 15).AddDays(offset) }, CancellationToken.None));
        var optimizedEdges = Enumerable.Range(4, 4).Select(offset =>
            client.SearchOneWayAsync(CreateSearchRequest() with { DepartureDate = new DateOnly(2026, 5, 15).AddDays(offset) }, CancellationToken.None));
        await Task.WhenAll(orderedEdges.Concat(optimizedEdges).Cast<Task>().Concat(new Task[] {
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None),
            client.SearchRoundTripAsync(new ProviderRoundTripSearchRequest("DUB", "AMS", new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 20), 1, "economy"), CancellationToken.None),
            client.SearchAirportsAsync("Dublin", CancellationToken.None)
        }));

        Assert.Equal(10, handler.CallCount);
        Assert.InRange(handler.MaximumConcurrentCalls, 1, 5);
    }

    [Fact]
    public async Task FailedAndCanceledRequests_ReleaseTheirPermit()
    {
        var failureGate = new RecordingGate();
        var failureClient = CreateClient(new StatusHttpMessageHandler(HttpStatusCode.InternalServerError), new RecordingProviderResponseCache(), failureGate);
        await Assert.ThrowsAsync<FlightApiResponseException>(() => failureClient.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None));
        Assert.Equal((1, 1), (failureGate.Acquisitions, failureGate.Releases));

        var cancellationGate = new RecordingGate();
        var cancellationClient = CreateClient(new CancelingHttpMessageHandler(), new RecordingProviderResponseCache(), cancellationGate);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellationClient.SearchOneWayAsync(CreateSearchRequest(), cancellation.Token));
        Assert.Equal((1, 1), (cancellationGate.Acquisitions, cancellationGate.Releases));
    }

    [Fact]
    public async Task ProviderError_ExposesBoundedSanitizedDiagnosticsAndCorrelationId()
    {
        var logger = new RecordingLogger<FlightApiClient>();
        var handler = new DiagnosticErrorHandler(
            HttpStatusCode.BadRequest,
            """{"message":"Invalid route for key test-key token=provider-secret","debug":"must-not-be-logged"}""",
            "provider-request-123");
        var client = CreateClient(handler, new RecordingProviderResponseCache(), logger: logger);

        var error = await Assert.ThrowsAsync<FlightApiResponseException>(() =>
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("provider-request-123", error.ProviderRequestId);
        Assert.Contains("Invalid route", error.ResponseSummary);
        Assert.Contains("[redacted]", error.ResponseSummary);
        Assert.DoesNotContain("test-key", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-be-logged", error.Message, StringComparison.Ordinal);
        Assert.Contains("provider-request-123", error.Message);

        var renderedLogs = string.Join('\n', logger.Messages);
        Assert.Contains("Invalid route", renderedLogs);
        Assert.Contains("provider-request-123", renderedLogs);
        Assert.DoesNotContain("test-key", renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret", renderedLogs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrottledRequest_HonorsRetryAfterAndRetriesWithinCallerTimeout()
    {
        var gate = new RecordingGate();
        var handler = new ThrottleThenSuccessHandler();
        var client = CreateClient(handler, new RecordingProviderResponseCache(), gate);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await client.SearchOneWayAsync(CreateSearchRequest(), timeout.Token);

        Assert.Equal(2, handler.CallCount);
        Assert.Equal((2, 2, 1), (gate.Acquisitions, gate.Releases, gate.ThrottledResponses));
    }

    [Fact]
    public async Task ConcurrentIdenticalCacheMisses_AreCoalescedBeforeTheLiveRequestGate()
    {
        var cache = new RecordingProviderResponseCache();
        var handler = new ConcurrentTrackingHttpMessageHandler(
            """{"itineraries":[],"legs":[],"segments":[],"places":[],"carriers":[],"agents":[]}""");
        var gate = new RecordingGate();
        var coalescer = new ProviderRequestCoalescer();
        var client = CreateClient(handler, cache, gate, coalescer);

        await Task.WhenAll(
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None),
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None),
            client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
        Assert.Equal((1, 1), (gate.Acquisitions, gate.Releases));
    }

    [Fact]
    public async Task StructuredLogs_NeverContainProviderCredentialsOrBookingTokens()
    {
        var logger = new RecordingLogger<FlightApiClient>();
        var handler = new RecordingHttpMessageHandler(
            """{"itineraries":[{"id":"safe","deepLink":"https://book.example/fare?token=booking-secret"}],"legs":[],"segments":[],"places":[],"carriers":[],"agents":[]}""");
        var client = CreateClient(handler, new RecordingProviderResponseCache(), logger: logger);

        await client.SearchOneWayAsync(CreateSearchRequest(), CancellationToken.None);

        var renderedLogs = string.Join('\n', logger.Messages);
        Assert.DoesNotContain("test-key", renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("booking-secret", renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", renderedLogs, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderSearchRequest CreateSearchRequest() =>
        new("DUB", "AMS", new DateOnly(2026, 5, 15), 1, "premium economy");

    private static FlightApiClient CreateClient(
        HttpMessageHandler handler,
        IProviderResponseCache cache,
        IFlightApiRequestGate? gate = null,
        IProviderRequestCoalescer? coalescer = null,
        ILogger<FlightApiClient>? logger = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.flightapi.io/") },
            Options.Create(new FlightApiOptions
            {
                ApiKey = "test-key",
                Currency = "EUR",
                MaxConcurrentRequests = 5
            }),
            cache,
            gate ?? CreateGate(),
            coalescer ?? new ProviderRequestCoalescer(),
            logger ?? NullLogger<FlightApiClient>.Instance);

    private static IFlightApiRequestGate CreateGate() =>
        new FlightApiRequestGate(Options.Create(new FlightApiOptions { MaxConcurrentRequests = 5 }));

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

    private sealed class ConcurrentTrackingHttpMessageHandler(string content) : HttpMessageHandler
    {
        private int _callCount;
        private int _concurrentCalls;
        private int _maximumConcurrentCalls;

        public int CallCount => Volatile.Read(ref _callCount);

        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var concurrentCalls = Interlocked.Increment(ref _concurrentCalls);
            UpdateMaximum(concurrentCalls);
            try
            {
                await Task.Delay(40, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8, "application/json")
                };
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentCalls);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var currentMaximum = Volatile.Read(ref _maximumConcurrentCalls);
                if (value <= currentMaximum || Interlocked.CompareExchange(ref _maximumConcurrentCalls, value, currentMaximum) == currentMaximum)
                {
                    return;
                }
            }
        }
    }

    private sealed class RecordingGate : IFlightApiRequestGate
    {
        public int Acquisitions { get; private set; }
        public int Releases { get; private set; }
        public int CacheHits { get; private set; }
        public int ThrottledResponses { get; private set; }
        public ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Acquisitions++; return ValueTask.FromResult<IDisposable>(new Release(() => Releases++)); }
        public void RecordCacheHit() => CacheHits++;
        public void RecordLiveCall() { }
        public void RecordThrottledResponse() => ThrottledResponses++;
        private sealed class Release(Action action) : IDisposable { private Action? _action = action; public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke(); }
    }

    private sealed class StatusHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class DiagnosticErrorHandler(HttpStatusCode statusCode, string body, string requestId) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("x-request-id", requestId);
            return Task.FromResult(response);
        }
    }

    private sealed class CancelingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation handler unexpectedly completed.");
        }
    }

    private sealed class ThrottleThenSuccessHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                var throttled = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(throttled);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{formatter(state, exception)} {exception?.Message}".Trim());
    }
}
