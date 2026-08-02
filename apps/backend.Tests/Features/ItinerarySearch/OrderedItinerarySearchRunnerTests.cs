using System.Collections.Concurrent;
using backend.Features.ItinerarySearch;
using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class OrderedItinerarySearchRunnerTests
{
    [Fact]
    public async Task ThreeLegAirportGroups_ProduceProgressiveCompleteBookableItineraries()
    {
        var provider = new FixtureProvider(request => Response(request, request.OriginAirport == "SNN" ? 80 : 100, 90));
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([
            Leg("one", ["DUB", "SNN"], ["AMS", "RTM"], new(2026, 9, 1)),
            Leg("two", ["AMS", "RTM"], ["CDG", "ORY"], new(2026, 9, 3)),
            Leg("three", ["CDG", "ORY"], ["BCN"], new(2026, 9, 5))
        ], 1, "economy", "recommended");

        await runner.RunAsync("three-leg", request, 25, CancellationToken.None);

        var session = await store.GetAsync("three-leg", CancellationToken.None);
        Assert.NotNull(session);
        Assert.Equal("completed", session.Status);
        Assert.NotEmpty(session.Results);
        Assert.All(session.Results, result =>
        {
            Assert.Equal(3, result.Legs.Count);
            Assert.Equal(3, result.BookingOptions.Count);
            Assert.All(result.BookingOptions, option => Assert.StartsWith("https://", option.Url));
        });
        Assert.Equal(10, provider.Requests.Count);
        Assert.Contains(store.History, snapshot => snapshot.Status == "running" && snapshot.Results.Count > 0);
        Assert.NotNull(session.OrderedLegs);
        Assert.Equal(3, session.OrderedLegs.Count);
        Assert.All(session.OrderedLegs, leg =>
        {
            Assert.Equal("faresFound", leg.Status);
            Assert.True(leg.FaresFound > 0);
            Assert.Equal(leg.AirportPairsScheduled, leg.AirportPairsCompleted);
        });
    }

    [Theory]
    [InlineData("sameAirport", 0, false)]
    [InlineData("allowSwitch", 1, true)]
    public async Task ContinuityRule_ExcludesOrWarnsAboutAirportChanges(string continuity, int expectedResults, bool warningExpected)
    {
        var provider = new FixtureProvider(request => Response(request, 100, 60));
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([
            Leg("one", ["DUB"], ["AMS"], new(2026, 9, 1)),
            Leg("two", ["RTM"], ["CDG"], new(2026, 9, 3), continuity)
        ], 1, "economy", "recommended");

        await runner.RunAsync("continuity", request, 10, CancellationToken.None);

        var results = (await store.GetAsync("continuity", CancellationToken.None))!.Results;
        Assert.Equal(expectedResults, results.Count);
        if (warningExpected) Assert.Contains(results[0].Warnings, warning => warning.Code == "airportSwitch");
    }

    [Fact]
    public async Task CheapestAndFastest_AreDeterministic()
    {
        var provider = new FixtureProvider(request => request.OriginAirport == "DUB" ? Response(request, 50, 180) : Response(request, 90, 60));
        var request = new OrderedTripRequest([Leg("one", ["DUB", "SNN"], ["AMS"], new(2026, 9, 1))], 1, "economy", "cheapest");
        var (runner, store) = CreateRunner(provider);
        await runner.RunAsync("rank", request, 10, CancellationToken.None);
        Assert.Equal("DUB", (await store.GetAsync("rank", CancellationToken.None))!.Results[0].Legs[0].OriginAirport);

        var (fastRunner, fastStore) = CreateRunner(provider);
        await fastRunner.RunAsync("fast", request with { Ranking = "fastest" }, 10, CancellationToken.None);
        Assert.Equal("SNN", (await fastStore.GetAsync("fast", CancellationToken.None))!.Results[0].Legs[0].OriginAirport);
    }

    [Fact]
    public async Task ProviderBudget_BoundsExpandedAirportPairsAndAddsCoverageWarning()
    {
        var provider = new FixtureProvider(request => Response(request, 100, 60));
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([Leg("one", ["DUB", "SNN"], ["AMS", "RTM"], new(2026, 9, 1))], 1, "economy", "recommended");
        await runner.RunAsync("budget", request, 2, CancellationToken.None);

        var session = (await store.GetAsync("budget", CancellationToken.None))!;
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal("bounded", session.Coverage.Mode);
        Assert.Contains(session.Warnings, warning => warning.Code == "providerBudgetReached");
    }

    [Fact]
    public async Task MissingLegFares_IdentifyTheLegThatPreventsACompleteItinerary()
    {
        var provider = new FixtureProvider(request => request.OriginAirport == "AMS" ? new FlightApiOneWayResponse() : Response(request, 100, 60));
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([
            Leg("one", ["DUB"], ["AMS"], new(2026, 9, 1)),
            Leg("two", ["AMS"], ["WAW"], new(2026, 9, 3))
        ], 1, "economy", "recommended");

        await runner.RunAsync("missing-leg", request, 10, CancellationToken.None);

        var session = (await store.GetAsync("missing-leg", CancellationToken.None))!;
        Assert.Empty(session.Results);
        Assert.NotNull(session.OrderedLegs);
        Assert.Collection(session.OrderedLegs,
            first =>
            {
                Assert.Equal("faresFound", first.Status);
                Assert.True(first.FaresFound > 0);
            },
            second =>
            {
                Assert.Equal("noFares", second.Status);
                Assert.Equal(0, second.FaresFound);
                Assert.Equal(second.AirportPairsScheduled, second.AirportPairsCompleted);
            });
        Assert.Contains(session.Warnings, warning => warning.Code == "noCompleteItinerary");
    }

    [Fact]
    public async Task ProviderBudget_ReportsLegsThatCouldNotBeSearched()
    {
        var provider = new FixtureProvider(request => Response(request, 100, 60));
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([
            Leg("one", ["DUB"], ["AMS"], new(2026, 9, 1)),
            Leg("two", ["AMS"], ["WAW"], new(2026, 9, 3))
        ], 1, "economy", "recommended");

        await runner.RunAsync("skipped-leg", request, 1, CancellationToken.None);

        var session = (await store.GetAsync("skipped-leg", CancellationToken.None))!;
        Assert.NotNull(session.OrderedLegs);
        Assert.Contains(session.OrderedLegs, leg => leg.Status == "limited" && leg.AirportPairsScheduled == 0);
    }

    [Fact]
    public async Task MaximumConfiguredOrderedInput_StaysWithinEveryRuntimeCollectionLimit()
    {
        var options = new MultiDestinationSearchOptions();
        var groups = Enumerable.Range(0, options.MaxOrderedLegs + 1)
            .Select(groupIndex => Enumerable.Range(0, options.MaxAirportsPerGroup)
                .Select(airportIndex => $"{(char)('A' + groupIndex)}{(char)('A' + airportIndex)}{(char)('K' + groupIndex)}")
                .ToList())
            .ToList();
        var request = new OrderedTripRequest(
            Enumerable.Range(0, options.MaxOrderedLegs)
                .Select(index => Leg($"max-{index}", groups[index], groups[index + 1], new DateOnly(2026, 9, 1).AddDays(index)))
                .ToList(),
            9,
            "first",
            "recommended");
        var provider = new FixtureProvider(providerRequest => Response(providerRequest, 100, 60));
        var (runner, store) = CreateRunner(provider, options: options);

        await runner.RunAsync("maximum-ordered", request, options.HardMaxProviderCalls, CancellationToken.None);

        var session = (await store.GetAsync("maximum-ordered", CancellationToken.None))!;
        Assert.Equal(options.MaxOrderedLegs * options.MaxAirportsPerGroup * options.MaxAirportsPerGroup, provider.Requests.Count);
        Assert.InRange(session.Results.Count, 0, options.MaxStoredResults);
        Assert.InRange(session.Coverage.CandidateStatesEvaluated, 0, options.MaxActiveStates);
        Assert.Equal("exhaustive", session.Coverage.Mode);
    }

    [Fact]
    public async Task CachedAirportPairs_AreEvaluatedWithoutConsumingTheLiveCallBudget()
    {
        var cachedRequest = new ProviderSearchRequest("DUB", "AMS", new(2026, 9, 1), 1, "economy");
        var cache = new FixtureCache(new Dictionary<string, object>
        {
            [ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(cachedRequest)] = Response(cachedRequest, 70, 80)
        });
        var provider = new FixtureProvider(request => Response(request, 90, 60));
        var (runner, store) = CreateRunner(provider, cache);
        var request = new OrderedTripRequest([Leg("one", ["DUB", "SNN"], ["AMS"], new(2026, 9, 1))], 1, "economy", "recommended");

        await runner.RunAsync("cache-budget", request, 1, CancellationToken.None);

        var session = (await store.GetAsync("cache-budget", CancellationToken.None))!;
        Assert.Single(provider.Requests);
        Assert.Equal("SNN", provider.Requests.Single().OriginAirport);
        Assert.Equal(2, session.Results.Count);
        Assert.Equal((1, 1, "exhaustive"), (session.Coverage.LiveProviderCallsUsed, session.Coverage.CacheHits, session.Coverage.Mode));
    }

    [Fact]
    public async Task RecommendedScoring_UsesDynamicTimeValueAndRetainsEveryRankingLeader()
    {
        var provider = new FixtureProvider(request => request.OriginAirport switch
        {
            "DUB" => Response(request, 50, 300),
            "SNN" => Response(request, 100, 60),
            _ => Response(request, 70, 120)
        });
        var options = new MultiDestinationSearchOptions { MaxCandidatesPerState = 25, MaxActiveStates = 1000, MaxStoredResults = 3 };
        var (runner, store) = CreateRunner(provider, options: options);
        var request = new OrderedTripRequest([Leg("one", ["DUB", "SNN", "ORK"], ["AMS"], new(2026, 9, 1))], 1, "economy", "recommended");

        await runner.RunAsync("ranking-union", request, 10, CancellationToken.None);

        var results = (await store.GetAsync("ranking-union", CancellationToken.None))!.Results;
        Assert.Equal(["ORK", "DUB", "SNN"], results.Select(result => result.Legs[0].OriginAirport).ToList());
        Assert.Equal(80m, results[0].RankingBreakdown.Score);
    }

    [Fact]
    public async Task ProgressAndCompletionWrites_CannotOverwriteCanceledSession()
    {
        var provider = new BlockingProvider();
        var (runner, store) = CreateRunner(provider);
        var request = new OrderedTripRequest([Leg("one", ["DUB"], ["AMS"], new(2026, 9, 1))], 1, "economy", "recommended");
        var running = runner.RunAsync("cancel-race", request, 10, CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await store.SetAsync(new("cancel-race", "ordered", "canceled", "canceled", 0, new(), [], []), CancellationToken.None);

        provider.Release.SetResult();
        await running;

        Assert.Equal("canceled", (await store.GetAsync("cancel-race", CancellationToken.None))!.Status);
        Assert.DoesNotContain(store.History, snapshot => snapshot.SearchId == "cancel-race" && snapshot.Status == "completed");
    }

    private static (OrderedItinerarySearchRunner Runner, ConcurrentStore Store) CreateRunner(IFlightSearchProvider provider, IProviderResponseCache? cache = null, MultiDestinationSearchOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IFlightSearchProvider>(_ => provider);
        var root = services.BuildServiceProvider();
        var store = new ConcurrentStore();
        var configured = Options.Create(options ?? new MultiDestinationSearchOptions { MaxCandidatesPerState = 25, MaxActiveStates = 1000, MaxStoredResults = 100 });
        return (new(root.GetRequiredService<IServiceScopeFactory>(), store, cache ?? new EmptyProviderCache(), new NoOpGate(), configured, NullLogger<OrderedItinerarySearchRunner>.Instance), store);
    }

    private static OrderedLegRequest Leg(string id, List<string> from, List<string> to, DateOnly date, string continuity = "sameAirport") =>
        new(id, new($"{id}-from", "From", from), new($"{id}-to", "To", to), date, continuity);

    private static FlightApiOneWayResponse Response(ProviderSearchRequest request, decimal price, int duration)
    {
        return new()
        {
            Places = [new() { Id = 1, Iata = request.OriginAirport }, new() { Id = 2, Iata = request.DestinationAirport }],
            Carriers = [new() { Id = 10, Name = "Test Air", DisplayCode = "TA" }],
            Segments = [new() { Id = "segment", OriginPlaceId = 1, DestinationPlaceId = 2, Departure = request.DepartureDate.ToDateTime(new(9, 0)), Arrival = request.DepartureDate.ToDateTime(new(9, 0)).AddMinutes(duration), Duration = duration, MarketingCarrierId = 10, MarketingFlightNumber = "101" }],
            Legs = [new() { Id = "provider-leg", OriginPlaceId = 1, DestinationPlaceId = 2, Departure = request.DepartureDate.ToDateTime(new(9, 0)), Arrival = request.DepartureDate.ToDateTime(new(9, 0)).AddMinutes(duration), Duration = duration, SegmentIds = ["segment"] }],
            Itineraries = [new() { Id = $"{request.OriginAirport}-{request.DestinationAirport}", LegIds = ["provider-leg"], DeepLink = $"https://book.example/{request.OriginAirport}-{request.DestinationAirport}", PricingOptions = [new() { Id = "price", Price = new() { Amount = price } }] }]
        };
    }

    private sealed class FixtureProvider(Func<ProviderSearchRequest, FlightApiOneWayResponse> responseFactory) : IFlightSearchProvider
    {
        public ConcurrentBag<ProviderSearchRequest> Requests { get; } = [];
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(ProviderSearchRequest request, CancellationToken cancellationToken) { Requests.Add(request); return Task.FromResult(responseFactory(request)); }
        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(ProviderRoundTripSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyProviderCache : IProviderResponseCache
    {
        public Task<T?> GetAsync<T>(string cacheKey, CancellationToken cancellationToken) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string cacheKey, T response, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixtureCache(Dictionary<string, object> values) : IProviderResponseCache
    {
        public Task<T?> GetAsync<T>(string cacheKey, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(cacheKey, out var value) ? (T?)value : default);
        public Task SetAsync<T>(string cacheKey, T response, CancellationToken cancellationToken)
        {
            values[cacheKey] = response!;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpGate : IFlightApiRequestGate
    {
        public ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The ordered runner must not acquire provider permits.");
        public void RecordCacheHit() { }
        public void RecordLiveCall() { }
        public void RecordThrottledResponse() { }
    }

    private sealed class BlockingProvider : IFlightSearchProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FlightApiOneWayResponse> SearchOneWayAsync(ProviderSearchRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Response(request, 100, 60);
        }
        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(ProviderRoundTripSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ConcurrentStore : IItinerarySearchSessionStore
    {
        private readonly ConcurrentDictionary<string, ItinerarySearchSessionResponse> _sessions = [];
        private readonly object _sync = new();
        public ConcurrentBag<ItinerarySearchSessionResponse> History { get; } = [];
        public Task SetAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken) { lock (_sync) _sessions[session.SearchId] = session; History.Add(session); return Task.CompletedTask; }
        public Task<bool> TrySetUnlessCanceledAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_sessions.GetValueOrDefault(session.SearchId)?.Status == "canceled") return Task.FromResult(false);
                _sessions[session.SearchId] = session;
                History.Add(session);
                return Task.FromResult(true);
            }
        }
        public Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken) { lock (_sync) return Task.FromResult(_sessions.GetValueOrDefault(searchId)); }
    }
}
