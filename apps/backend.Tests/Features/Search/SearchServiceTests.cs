using backend.Features.Search;
using backend.Features.Search.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace backend.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task StartSearchAsync_Completes_WhenIntermediateSessionPersistenceFails()
    {
        var store = new FlakySearchSessionStore(failingCalls: [2]);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(CreateRequest(), CancellationToken.None);

        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(1, finalSession.CompletedCombinations);
        Assert.Equal(0, finalSession.FailedCombinations);
        Assert.Null(finalSession.ErrorMessage);
    }

    [Fact]
    public async Task StartSearchAsync_MarksSearchFailed_WhenFailedSnapshotPersistenceFails()
    {
        var store = new FlakySearchSessionStore(failingCalls: [2]);
        var service = CreateSearchService(store, new ThrowingFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(CreateRequest(), CancellationToken.None);

        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("failed", finalSession.Status);
        Assert.Equal(1, finalSession.CompletedCombinations);
        Assert.Equal(1, finalSession.FailedCombinations);
        Assert.Equal("No results could be retrieved from the flight provider.", finalSession.ErrorMessage);
    }

    [Fact]
    public async Task StartSearchAsync_AllowsRequestsWhoseExpandedCombinationsStayWithinLimit()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new RecordingFlightSearchProvider();
        var service = CreateSearchService(store, provider);

        var request = new SearchRequest(
            OriginAirports:
            [
                "DUB", "DUB", "ORK", "SNN", "NOC", "KIR", "WAT"
            ],
            DestinationAirports:
            [
                "AMS", "BCN", "CDG", "FRA", "MAD", "LHR", "OSL", "CPH", "MXP", "ARN"
            ],
            DepartDateFrom: new DateOnly(2026, 5, 15),
            DepartDateTo: new DateOnly(2026, 5, 15),
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal(60, initialSession.TotalCombinations);
        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(60, finalSession.CompletedCombinations);
        Assert.Equal(60, provider.Requests.Count);
    }

    [Fact]
    public async Task StartSearchAsync_RejectsRequestsWhoseExpandedCombinationsExceedLimit()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var request = new SearchRequest(
            OriginAirports: ["DUB", "ORK", "SNN", "NOC", "KIR", "WAT", "SXL"],
            DestinationAirports: ["AMS", "BCN", "CDG", "FRA", "MAD", "LHR", "OSL", "CPH", "MXP"],
            DepartDateFrom: new DateOnly(2026, 5, 15),
            DepartDateTo: new DateOnly(2026, 5, 15),
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, CancellationToken.None));

        Assert.Equal("Search exceeds the limit of 60 combinations.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_PreservesProviderLocalTimes_WithoutForcingUtc()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new TimedFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(CreateRequest(), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);
        var result = Assert.Single(finalSession.Response.Results);
        var leg = Assert.Single(result.Legs);
        var segment = Assert.Single(leg.Segments);

        Assert.Equal(DateTimeKind.Unspecified, leg.DepartureUtc.Kind);
        Assert.Equal(DateTimeKind.Unspecified, leg.ArrivalUtc.Kind);
        Assert.Equal(new DateTime(2026, 5, 15, 6, 30, 0), leg.DepartureUtc);
        Assert.Equal(new DateTime(2026, 5, 15, 9, 15, 0), leg.ArrivalUtc);
        Assert.Equal(DateTimeKind.Unspecified, segment.DepartureUtc.Kind);
        Assert.Equal(DateTimeKind.Unspecified, segment.ArrivalUtc.Kind);
        Assert.Equal(new DateTime(2026, 5, 15, 6, 30, 0), segment.DepartureUtc);
        Assert.Equal(new DateTime(2026, 5, 15, 9, 15, 0), segment.ArrivalUtc);
    }

    private static SearchRequest CreateRequest() =>
        new(
            ["DUB"],
            ["AMS"],
            new DateOnly(2026, 5, 15),
            new DateOnly(2026, 5, 15),
            null,
            null,
            1,
            "economy");

    private static ISearchService CreateSearchService(
        ISearchSessionStore store,
        IFlightSearchProvider flightSearchProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISearchSessionStore>(store);
        services.AddSingleton<IFlightSearchProvider>(flightSearchProvider);
        services.AddSingleton<ISearchService, SearchService>();

        var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<ISearchService>();
    }

    private sealed class SuccessfulFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());
    }

    private sealed class RecordingFlightSearchProvider : IFlightSearchProvider
    {
        private readonly Lock _lock = new();
        private readonly List<ProviderSearchRequest> _requests = [];

        public IReadOnlyList<ProviderSearchRequest> Requests
        {
            get
            {
                lock (_lock)
                {
                    return [.. _requests];
                }
            }
        }

        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _requests.Add(request);
            }

            return Task.FromResult(new FlightApiOneWayResponse());
        }
    }

    private sealed class ThrowingFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider failure");
    }

    private sealed class TimedFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse
            {
                Itineraries =
                [
                    new FlightApiItinerary
                    {
                        Id = "itinerary-1",
                        LegIds = ["leg-1"],
                        DeepLink = "https://example.com/fare",
                        PricingOptions =
                        [
                            new FlightApiPricingOption
                            {
                                Id = "pricing-1",
                                Price = new FlightApiPrice { Amount = 199m },
                                Items = []
                            }
                        ]
                    }
                ],
                Legs =
                [
                    new FlightApiLeg
                    {
                        Id = "leg-1",
                        OriginPlaceId = 1,
                        DestinationPlaceId = 2,
                        Departure = new DateTime(2026, 5, 15, 6, 30, 0),
                        Arrival = new DateTime(2026, 5, 15, 9, 15, 0),
                        SegmentIds = ["segment-1"],
                        Duration = 165
                    }
                ],
                Segments =
                [
                    new FlightApiSegment
                    {
                        Id = "segment-1",
                        OriginPlaceId = 1,
                        DestinationPlaceId = 2,
                        Departure = new DateTime(2026, 5, 15, 6, 30, 0),
                        Arrival = new DateTime(2026, 5, 15, 9, 15, 0),
                        Duration = 165,
                        MarketingFlightNumber = "123",
                        MarketingCarrierId = 1
                    }
                ],
                Places =
                [
                    new FlightApiPlace { Id = 1, Iata = "DUB", DisplayCode = "DUB", Name = "Dublin" },
                    new FlightApiPlace { Id = 2, Iata = "AMS", DisplayCode = "AMS", Name = "Amsterdam" }
                ],
                Carriers =
                [
                    new FlightApiCarrier { Id = 1, Name = "Test Airline", DisplayCode = "TA" }
                ]
            });
    }

    private sealed class FlakySearchSessionStore(IEnumerable<int> failingCalls) : ISearchSessionStore
    {
        private readonly HashSet<int> _failingCalls = [.. failingCalls];
        private readonly Lock _lock = new();
        private readonly Dictionary<string, SearchSessionResponse> _sessions = [];
        private int _setCallCount;

        public Task<SearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _sessions.TryGetValue(searchId, out var session);
                return Task.FromResult<SearchSessionResponse?>(session);
            }
        }

        public Task SetAsync(SearchSessionResponse session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _setCallCount += 1;
                if (_failingCalls.Contains(_setCallCount))
                {
                    throw new InvalidOperationException($"Injected persistence failure on call {_setCallCount}.");
                }

                _sessions[session.SearchId] = session;
            }

            return Task.CompletedTask;
        }

        public async Task<SearchSessionResponse> WaitForTerminalSessionAsync(
            string searchId,
            int timeoutMilliseconds = 5000)
        {
            var startedAt = Environment.TickCount64;

            while (Environment.TickCount64 - startedAt < timeoutMilliseconds)
            {
                var session = await GetAsync(searchId, CancellationToken.None);
                if (session is not null && session.Status is "completed" or "failed")
                {
                    return session;
                }

                await Task.Delay(25);
            }

            throw new TimeoutException("Timed out waiting for a terminal search session.");
        }
    }
}
