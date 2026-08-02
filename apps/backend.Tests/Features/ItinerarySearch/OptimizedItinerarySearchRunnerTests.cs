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

public sealed class OptimizedItinerarySearchRunnerTests
{
    [Fact]
    public async Task DeterministicGraph_ReturnsCorrectCheapestFastestAndRecommendedRoutes()
    {
        var provider = new FixtureProvider(request => request.OriginAirport switch
        {
            "DUB" => Response(request, 50, 300),
            "SNN" => Response(request, 100, 60),
            _ => Response(request, 70, 120)
        });
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN", "ORK") };

        var cheapest = await RunAsync("cheapest", request with { Ranking = "cheapest" }, provider);
        var fastest = await RunAsync("fastest", request with { Ranking = "fastest" }, provider);
        var recommended = await RunAsync("recommended", request with { Ranking = "recommended" }, provider);

        Assert.Equal("DUB", cheapest.Results[0].Legs[0].OriginAirport);
        Assert.Equal("SNN", fastest.Results[0].Legs[0].OriginAirport);
        Assert.Equal("ORK", recommended.Results[0].Legs[0].OriginAirport);
        Assert.Equal(80m, recommended.Results[0].RankingBreakdown.Score);
    }

    [Fact]
    public async Task OpenEndedMode_ChoosesTheCheapestFinalDestination()
    {
        var provider = new FixtureProvider(request =>
        {
            var price = (request.OriginAirport, request.DestinationAirport) switch
            {
                ("DUB", "AMS") or ("AMS", "CDG") => 20,
                _ => 100
            };
            return Response(request, price, 60);
        });
        var request = OneDestinationRequest() with
        {
            Destinations = [Destination("ams", "AMS"), Destination("cdg", "CDG")],
            EndDate = new(2026, 9, 5),
            Ranking = "cheapest"
        };

        var session = await RunAsync("open-ended", request, provider);

        Assert.Equal(["ams", "cdg"], session.Results[0].DestinationOrder);
        Assert.Equal("CDG", session.Results[0].Legs[^1].DestinationAirport);
    }

    [Theory]
    [InlineData("returnToStart", "DUB")]
    [InlineData("fixedEnd", "LHR")]
    public async Task ClosedEndpointModes_TerminateOnlyAtTheRequiredGroup(string endpointMode, string expectedAirport)
    {
        var provider = new FixtureProvider(request => Response(request, 50, 60));
        var request = OneDestinationRequest() with
        {
            EndpointMode = endpointMode,
            FixedEnd = endpointMode == "fixedEnd" ? Group("finish", "LHR") : null,
            EndDate = new(2026, 9, 2),
            Destinations = [new(Group("ams", "AMS"), new("exactNights", 1))]
        };

        var session = await RunAsync(endpointMode, request, provider);

        Assert.NotEmpty(session.Results);
        Assert.All(session.Results, result => Assert.Equal(expectedAirport, result.Legs[^1].DestinationAirport));
    }

    [Theory]
    [InlineData("sameAirport", 0, false)]
    [InlineData("allowSwitch", 1, true)]
    public async Task AirportContinuity_RejectsOrWarnsAboutSwitches(string continuity, int expectedResults, bool warningExpected)
    {
        var provider = new FixtureProvider(request => (request.OriginAirport, request.DestinationAirport) switch
        {
            ("DUB", "AMS") => Response(request, 50, 60),
            ("RTM", "DUB") => Response(request, 50, 60),
            _ => new FlightApiOneWayResponse()
        });
        var request = OneDestinationRequest() with
        {
            EndpointMode = "returnToStart",
            EndDate = new(2026, 9, 2),
            Destinations = [new(Group("netherlands", "AMS", "RTM"), new("exactNights", 1), continuity)]
        };

        var session = await RunAsync($"continuity-{continuity}", request, provider);

        Assert.Equal(expectedResults, session.Results.Count);
        if (warningExpected) Assert.Contains(session.Results[0].Warnings, warning => warning.Code == "airportSwitch");
    }

    [Fact]
    public async Task ExactStay_UsesTheActualLocalArrivalDateAfterAnOvernightFlight()
    {
        var provider = new FixtureProvider(request =>
        {
            var response = Response(request, 50, 60);
            if (request.OriginAirport == "DUB")
            {
                var overnightArrival = request.DepartureDate.AddDays(1).ToDateTime(new(0, 30));
                response.Legs[0] = response.Legs[0] with { Arrival = overnightArrival, Duration = 930 };
                response.Segments[0] = response.Segments[0] with { Arrival = overnightArrival, Duration = 930 };
            }
            return response;
        });
        var request = OneDestinationRequest() with
        {
            EndpointMode = "returnToStart",
            EndDate = new(2026, 9, 4),
            Destinations = [new(Group("ams", "AMS"), new("exactNights", 2))]
        };

        var session = await RunAsync("overnight-exact", request, provider);

        var itinerary = Assert.Single(session.Results);
        Assert.Equal(new DateTime(2026, 9, 2, 0, 30, 0), itinerary.Legs[0].ArrivalLocalTime);
        Assert.Equal(new DateTime(2026, 9, 4, 9, 0, 0), itinerary.Legs[1].DepartureLocalTime);
        Assert.Equal(2, itinerary.Stays[0].Nights);
    }

    [Fact]
    public async Task ParetoFrontier_RemovesAStateDominatedByPriceAndDuration()
    {
        var provider = new FixtureProvider(request =>
        {
            var response = Response(request, 50, 60);
            response.Itineraries[0].PricingOptions.Add(new() { Id = "dominated", Price = new() { Amount = 100 } });
            return response;
        });

        var session = await RunAsync("pareto", OneDestinationRequest(), provider);

        Assert.Single(session.Results);
        Assert.Equal(50, session.Results[0].TotalPrice);
        Assert.True(session.Coverage.CandidateStatesPruned > 0);
    }

    [Fact]
    public async Task PerEdgeAllowance_ExploresUnseenEdgesBeforeTheGlobalStateBudgetIsConsumed()
    {
        var options = new MultiDestinationSearchOptions
        {
            MaxActiveStates = 2,
            MaxEvaluatedStates = 7,
            MaxCandidatesPerState = 10,
            MaxStoredResults = 10
        };
        var provider = new FixtureProvider(request => RichResponse(request, 6));
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN", "ORK") };

        var session = await RunAsync("edge-fairness", request, provider, providerCallLimit: 3, options: options);

        Assert.Equal(3, provider.Requests.Select(item => item.OriginAirport).Distinct().Count());
        Assert.Equal(6, session.Coverage.CandidateStatesEvaluated - session.Feasibility!.GeneratedScheduleCount);
        Assert.Contains(session.Warnings, warning => warning.Code == "edgeStateLimitReached");
        Assert.DoesNotContain(session.Warnings, warning => warning.Code == "stateEvaluationBudgetReached");
    }

    [Fact]
    public async Task FrontierAndResultLimits_PruneRetainedCandidatesWithoutStoppingEvaluation()
    {
        var options = new MultiDestinationSearchOptions
        {
            MaxActiveStates = 1,
            MaxEvaluatedStates = 10,
            MaxCandidatesPerState = 10,
            MaxStoredResults = 10
        };
        var provider = new FixtureProvider(request => Response(request, 50, 60));
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN", "ORK") };

        var session = await RunAsync("limits", request, provider, providerCallLimit: 3, options: options);

        Assert.Equal(3, provider.Requests.Count);
        Assert.Single(session.Results);
        Assert.True(session.Coverage.CandidateStatesEvaluated > options.MaxActiveStates);
        Assert.Contains(session.Warnings, warning => warning.Code == "frontierStateLimitReached");
        Assert.DoesNotContain(session.Warnings, warning => warning.Code == "stateEvaluationBudgetReached");
        Assert.Equal("bounded", session.Coverage.Mode);
    }

    [Fact]
    public async Task CumulativeEvaluationBudget_StopsOnlyAfterItsSeparateLimit()
    {
        var options = new MultiDestinationSearchOptions
        {
            MaxActiveStates = 2,
            MaxEvaluatedStates = 2,
            MaxCandidatesPerState = 10,
            MaxStoredResults = 10
        };
        var provider = new FixtureProvider(request => Response(request, 50, 60));
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN", "ORK") };

        var session = await RunAsync("evaluation-limit", request, provider, providerCallLimit: 3, options: options);

        Assert.Contains(session.Warnings, warning => warning.Code == "stateEvaluationBudgetReached");
        Assert.Equal("bounded", session.Coverage.Mode);
        Assert.True(session.Coverage.CandidateStatesEvaluated >= options.MaxEvaluatedStates);
    }

    [Fact]
    public async Task CachedEdges_DoNotConsumeTheLiveCallBudget()
    {
        var cachedRequest = new ProviderSearchRequest("DUB", "AMS", new(2026, 9, 1), 1, "economy");
        var cache = new FixtureCache(new Dictionary<string, object>
        {
            [ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(cachedRequest)] = Response(cachedRequest, 40, 120)
        });
        var provider = new FixtureProvider(request => Response(request, 60, 60));
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN") };

        var session = await RunAsync("cache", request, provider, providerCallLimit: 1, cache: cache);

        Assert.Single(provider.Requests);
        Assert.Equal(2, session.Results.Count);
        Assert.Equal((1, 1, "exhaustive"), (session.Coverage.LiveProviderCallsUsed, session.Coverage.CacheHits, session.Coverage.Mode));
    }

    [Fact]
    public async Task PartialProviderFailure_PreservesCompleteResultsAndReturnsPartialStatus()
    {
        var provider = new FixtureProvider(request => request.OriginAirport == "SNN"
            ? throw new HttpRequestException("fixture failure")
            : Response(request, 50, 60));
        var request = OneDestinationRequest() with { Start = Group("start", "DUB", "SNN") };

        var session = await RunAsync("partial", request, provider);

        Assert.Equal("partial", session.Status);
        Assert.NotEmpty(session.Results);
        Assert.Contains(session.Warnings, warning => warning.Code == "providerCallsFailed");
        Assert.Contains(session.Results[0].Warnings, warning => warning.Code == "separateTickets");
    }

    [Fact]
    public async Task CompleteItineraries_ArePersistedProgressively()
    {
        var provider = new FixtureProvider(request => Response(request, 50, 60));
        var (runner, store, plan) = CreateRunner(OneDestinationRequest(), provider);

        await runner.RunAsync("progressive", OneDestinationRequest(), plan, 25, CancellationToken.None);

        Assert.Contains(store.History, snapshot => snapshot.Status == "running" && snapshot.Results.Count > 0);
        Assert.Equal("completed", (await store.GetAsync("progressive", CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Cancellation_StopsTheWorkerAndPersistsTimeoutWhenTheSessionWasNotExplicitlyCanceled()
    {
        var provider = new BlockingProvider();
        var request = OneDestinationRequest();
        var (runner, store, plan) = CreateRunner(request, provider);
        using var cancellation = new CancellationTokenSource();
        var running = runner.RunAsync("timeout", request, plan, 25, cancellation.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await running;

        var session = (await store.GetAsync("timeout", CancellationToken.None))!;
        Assert.Equal(("failed", "timeout", "bounded"), (session.Status, session.Phase, session.Coverage.Mode));
        Assert.Contains(session.Warnings, warning => warning.Code == "executionTimeout");
    }

    private static async Task<ItinerarySearchSessionResponse> RunAsync(
        string searchId,
        OptimizedTripRequest request,
        IFlightSearchProvider provider,
        int providerCallLimit = 25,
        IProviderResponseCache? cache = null,
        MultiDestinationSearchOptions? options = null)
    {
        var (runner, store, plan) = CreateRunner(request, provider, cache, options);
        await runner.RunAsync(searchId, request, plan, providerCallLimit, CancellationToken.None);
        return (await store.GetAsync(searchId, CancellationToken.None))!;
    }

    private static (OptimizedItinerarySearchRunner Runner, ConcurrentStore Store, OptimizedSchedulePlan Plan) CreateRunner(
        OptimizedTripRequest request,
        IFlightSearchProvider provider,
        IProviderResponseCache? cache = null,
        MultiDestinationSearchOptions? options = null)
    {
        options ??= new MultiDestinationSearchOptions { MaxActiveStates = 1000, MaxCandidatesPerState = 25, MaxStoredResults = 100 };
        var services = new ServiceCollection();
        services.AddScoped<IFlightSearchProvider>(_ => provider);
        var root = services.BuildServiceProvider();
        var store = new ConcurrentStore();
        var configured = Options.Create(options);
        var runner = new OptimizedItinerarySearchRunner(
            root.GetRequiredService<IServiceScopeFactory>(),
            store,
            cache ?? new EmptyProviderCache(),
            new NoOpGate(),
            configured,
            NullLogger<OptimizedItinerarySearchRunner>.Instance);
        var plan = new OptimizedScheduleGenerator(configured).Generate(request);
        return (runner, store, plan);
    }

    private static OptimizedTripRequest OneDestinationRequest() => new(
        Group("start", "DUB"),
        [Destination("ams", "AMS")],
        "openEnded",
        null,
        new(2026, 9, 1),
        new(2026, 9, 3),
        "sameAirport",
        1,
        "economy",
        "recommended");

    private static DestinationRequest Destination(string id, string airport) =>
        new(Group(id, airport), new("minimumNights", 1));

    private static AirportGroupRequest Group(string id, params string[] airports) =>
        new(id, id, [.. airports]);

    private static FlightApiOneWayResponse Response(ProviderSearchRequest request, decimal price, int duration)
    {
        var departure = request.DepartureDate.ToDateTime(new(9, 0));
        return new()
        {
            Places = [new() { Id = 1, Iata = request.OriginAirport }, new() { Id = 2, Iata = request.DestinationAirport }],
            Carriers = [new() { Id = 10, Name = "Test Air", DisplayCode = "TA" }],
            Segments = [new() { Id = "segment", OriginPlaceId = 1, DestinationPlaceId = 2, Departure = departure, Arrival = departure.AddMinutes(duration), Duration = duration, MarketingCarrierId = 10, MarketingFlightNumber = "101" }],
            Legs = [new() { Id = "provider-leg", OriginPlaceId = 1, DestinationPlaceId = 2, Departure = departure, Arrival = departure.AddMinutes(duration), Duration = duration, SegmentIds = ["segment"] }],
            Itineraries = [new() { Id = $"{request.OriginAirport}-{request.DestinationAirport}-{request.DepartureDate:yyyyMMdd}", LegIds = ["provider-leg"], DeepLink = $"https://book.example/{request.OriginAirport}-{request.DestinationAirport}", PricingOptions = [new() { Id = "price", Price = new() { Amount = price } }] }]
        };
    }

    private static FlightApiOneWayResponse RichResponse(ProviderSearchRequest request, int count)
    {
        var response = new FlightApiOneWayResponse
        {
            Places = [new() { Id = 1, Iata = request.OriginAirport }, new() { Id = 2, Iata = request.DestinationAirport }],
            Carriers = [new() { Id = 10, Name = "Test Air", DisplayCode = "TA" }]
        };
        for (var index = 0; index < count; index++)
        {
            var departure = request.DepartureDate.ToDateTime(new(7 + index, 0));
            var duration = 60 + index * 10;
            var segmentId = $"segment-{index}";
            var legId = $"provider-leg-{index}";
            response.Segments.Add(new() { Id = segmentId, OriginPlaceId = 1, DestinationPlaceId = 2, Departure = departure, Arrival = departure.AddMinutes(duration), Duration = duration, MarketingCarrierId = 10, MarketingFlightNumber = $"10{index}" });
            response.Legs.Add(new() { Id = legId, OriginPlaceId = 1, DestinationPlaceId = 2, Departure = departure, Arrival = departure.AddMinutes(duration), Duration = duration, SegmentIds = [segmentId] });
            response.Itineraries.Add(new() { Id = $"itinerary-{index}", LegIds = [legId], DeepLink = $"https://book.example/{index}", PricingOptions = [new() { Id = $"price-{index}", Price = new() { Amount = 100 - index * 5 } }] });
        }
        return response;
    }

    private sealed class FixtureProvider(Func<ProviderSearchRequest, FlightApiOneWayResponse> responseFactory) : IFlightSearchProvider
    {
        public ConcurrentBag<ProviderSearchRequest> Requests { get; } = [];
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(ProviderSearchRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(ProviderRoundTripSearchRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class BlockingProvider : IFlightSearchProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FlightApiOneWayResponse> SearchOneWayAsync(ProviderSearchRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking provider unexpectedly completed.");
        }
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
        public ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("The optimizer must not acquire provider permits outside FlightApiClient.");
        public void RecordCacheHit() { }
        public void RecordLiveCall() { }
        public void RecordThrottledResponse() { }
    }

    private sealed class ConcurrentStore : IItinerarySearchSessionStore
    {
        private readonly ConcurrentDictionary<string, ItinerarySearchSessionResponse> _sessions = [];
        private readonly object _sync = new();
        public ConcurrentBag<ItinerarySearchSessionResponse> History { get; } = [];
        public Task SetAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken)
        {
            lock (_sync) _sessions[session.SearchId] = session;
            History.Add(session);
            return Task.CompletedTask;
        }
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
        public Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken)
        {
            lock (_sync) return Task.FromResult(_sessions.GetValueOrDefault(searchId));
        }
    }
}
