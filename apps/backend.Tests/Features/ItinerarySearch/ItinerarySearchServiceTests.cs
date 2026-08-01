using backend.Features.ItinerarySearch;
using backend.Features.ItinerarySearch.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace backend.Tests;

public sealed class ItinerarySearchServiceTests
{
    public static IEnumerable<object[]> NonPositiveOptionValues()
    {
        yield return [new MultiDestinationSearchOptions { SessionTtlMinutes = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxOptimizedDestinations = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxAirportsPerGroup = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxTripDays = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxOrderedLegs = 0 }];
        yield return [new MultiDestinationSearchOptions { AnonymousMaxProviderCalls = 0 }];
        yield return [new MultiDestinationSearchOptions { UserMaxProviderCalls = 0 }];
        yield return [new MultiDestinationSearchOptions { AdminMaxProviderCalls = 0 }];
        yield return [new MultiDestinationSearchOptions { HardMaxProviderCalls = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxActiveStates = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxCandidatesPerState = 0 }];
        yield return [new MultiDestinationSearchOptions { MaxStoredResults = 0 }];
        yield return [new MultiDestinationSearchOptions { ExecutionTimeoutMinutes = 0 }];
    }

    [Theory]
    [MemberData(nameof(NonPositiveOptionValues))]
    public void ConfigurationValidation_RejectsEveryNonPositiveLimit(MultiDestinationSearchOptions options)
    {
        Assert.False(MultiDestinationSearchOptionsValidation.HasPositiveLimits(options));
    }

    [Fact]
    public void ConfigurationValidation_RejectsOutOfOrderProviderBudgets()
    {
        Assert.False(MultiDestinationSearchOptionsValidation.HasOrderedProviderBudgets(new() { AnonymousMaxProviderCalls = 101, UserMaxProviderCalls = 100 }));
    }

    [Fact]
    public async Task ValidOrderedAndOptimizedRequests_CreateRunningSessions()
    {
        var service = CreateService();
        var ordered = await service.StartAsync(CreateOrderedRequest(), 25, CancellationToken.None);
        var optimized = await service.StartAsync(CreateOptimizedRequest(), 25, CancellationToken.None);

        Assert.Equal(("ordered", "running", "validating"), (ordered.Mode, ordered.Status, ordered.Phase));
        Assert.Equal(("optimize", "running", "validating"), (optimized.Mode, optimized.Status, optimized.Phase));
    }

    [Fact]
    public async Task InvalidInput_IdentifiesTheFieldAndConfiguredLimit()
    {
        var service = CreateService(new MultiDestinationSearchOptions { MaxOrderedLegs = 1 });
        var request = CreateOrderedRequest() with { Legs = [.. CreateOrderedRequest().Legs, CreateOrderedRequest().Legs[0] with { Id = "leg-2" }] };

        var error = await Assert.ThrowsAsync<ItineraryValidationException>(() => service.StartAsync(request, 25, CancellationToken.None));

        Assert.Equal("legs", error.Field);
        Assert.Contains("1", error.Message);
    }

    [Fact]
    public async Task Cancellation_IsStableAndPersisted()
    {
        var store = new MemoryStore();
        var service = CreateService(store: store);
        var running = await service.StartAsync(CreateOrderedRequest(), 25, CancellationToken.None);

        var first = await service.CancelAsync(running.SearchId, CancellationToken.None);
        var second = await service.CancelAsync(running.SearchId, CancellationToken.None);

        Assert.Equal("canceled", first?.Status);
        Assert.Equal(first, second);
        Assert.Equal(first, await store.GetAsync(running.SearchId, CancellationToken.None));
    }

    [Fact]
    public void RequestDiscriminator_AcceptsSupportedModesAndRejectsUnknownMode()
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string orderedJson = """{"mode":"ordered","legs":[],"adults":1,"cabinClass":"economy","ranking":"recommended"}""";
        const string optimizedJson = """{"mode":"optimize","start":{"id":"start","label":"Start","airportCodes":["DUB"]},"destinations":[],"endpointMode":"openEnded","fixedEnd":null,"startDate":"2026-09-01","endDate":"2026-09-02","defaultAirportContinuity":"sameAirport","adults":1,"cabinClass":"economy","ranking":"recommended"}""";

        Assert.IsType<OrderedTripRequest>(JsonSerializer.Deserialize<ItinerarySearchRequest>(orderedJson, serializerOptions));
        Assert.IsType<OptimizedTripRequest>(JsonSerializer.Deserialize<ItinerarySearchRequest>(optimizedJson, serializerOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ItinerarySearchRequest>(orderedJson.Replace("ordered", "unknown"), serializerOptions));
    }

    [Fact]
    public async Task OrderedFilters_UseAllLegSemanticsAndBookingRiskControls()
    {
        var store = new MemoryStore();
        var service = CreateService(store: store);
        var direct = Result("direct", [Leg("DUB", "AMS", 0, "TA"), Leg("AMS", "CDG", 0, "TA")], 2, 0);
        var mixed = Result("mixed", [Leg("DUB", "AMS", 0, "TA"), Leg("AMS", "CDG", 1, "TB")], 3, 1);
        await store.SetAsync(new("filters", "ordered", "completed", "completed", 100, new(), [direct, mixed], []), CancellationToken.None);

        var filtered = await service.GetAsync("filters", new ItineraryResultsQuery
        {
            Direct = true,
            Airlines = "TA",
            MaxBookingCount = 2,
            AllowAirportSwitches = false,
            Page = 1,
            PageSize = 1
        }, CancellationToken.None);

        Assert.Equal("direct", Assert.Single(filtered!.Results).Id);
        Assert.Equal((1, 1), (filtered.Pagination!.TotalResults, filtered.Pagination.PageSize));
        Assert.Contains(filtered.Filters!.Airlines, option => option.Value == "TA");
    }

    [Fact]
    public async Task OrderedFilters_ApplyBookingTypeAndRealMultiPagePagination()
    {
        var store = new MemoryStore();
        var service = CreateService(store: store);
        var leg = Leg("DUB", "AMS", 0, "TA");
        var separateA = Result("separate-a", [leg], 1, 0);
        var separateB = Result("separate-b", [leg], 1, 0);
        var bundled = Result("bundled", [leg], 1, 0) with { BookingType = "bundledFare" };
        await store.SetAsync(new("pagination", "ordered", "completed", "completed", 100, new(), [separateB, bundled, separateA], []), CancellationToken.None);

        var secondPage = await service.GetAsync("pagination", new ItineraryResultsQuery
        {
            BookingType = "separateTickets",
            Ranking = "cheapest",
            Page = 2,
            PageSize = 1
        }, CancellationToken.None);

        Assert.Equal("separate-b", Assert.Single(secondPage!.Results).Id);
        Assert.Equal((2, 1, 2, 2), (secondPage.Pagination!.Page, secondPage.Pagination.PageSize, secondPage.Pagination.TotalResults, secondPage.Pagination.TotalPages));

        var bundledOnly = await service.GetAsync("pagination", new ItineraryResultsQuery { BookingType = "bundledFare" }, CancellationToken.None);
        Assert.Equal("bundled", Assert.Single(bundledOnly!.Results).Id);
    }

    internal static OrderedTripRequest CreateOrderedRequest() => new(
        [new OrderedLegRequest("leg-1", Group("start", "DUB"), Group("end", "AMS"), new DateOnly(2026, 9, 1))], 1, "economy", "recommended");

    internal static OptimizedTripRequest CreateOptimizedRequest() => new(
        Group("start", "DUB"),
        [new DestinationRequest(Group("destination", "AMS"), new StayRuleRequest("minimumNights", 2))],
        "returnToStart", null, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), "sameAirport", 1, "economy", "recommended");

    private static AirportGroupRequest Group(string id, string code) => new(id, id, [code]);

    private static ItineraryLeg Leg(string origin, string destination, int stops, string airline) => new(
        $"{origin}-{destination}", origin, destination, new(2026, 9, 1, 9, 0, 0), new(2026, 9, 1, 10, 0, 0), 60, stops,
        [new(airline, airline, "1", origin, destination, new(2026, 9, 1, 9, 0, 0), new(2026, 9, 1, 10, 0, 0), 60)]);

    private static ItineraryResult Result(string id, List<ItineraryLeg> legs, int bookings, int switches) => new(
        id, "separateTickets", legs.Select(leg => leg.DestinationAirport).ToList(), legs, [], 200, "EUR", legs.Sum(leg => leg.DurationMinutes), legs.Sum(leg => leg.Stops), bookings, switches,
        Enumerable.Range(1, bookings).Select(index => new BookingOption($"Book {index}", $"https://book.example/{id}/{index}", 100, "EUR", "FlightApi")).ToList(), [], new(0, 200, 0, legs.Sum(leg => leg.Stops), bookings - 1, switches));

    private static ItinerarySearchService CreateService(MultiDestinationSearchOptions? options = null, MemoryStore? store = null) =>
        new(store ?? new MemoryStore(), new NoOpRunner(), Options.Create(options ?? new MultiDestinationSearchOptions()));

    internal sealed class NoOpRunner : IOrderedItinerarySearchRunner
    {
        public Task RunAsync(string searchId, OrderedTripRequest request, int providerCallLimit, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    internal sealed class MemoryStore : IItinerarySearchSessionStore
    {
        private readonly Dictionary<string, ItinerarySearchSessionResponse> _sessions = [];
        private readonly object _sync = new();
        public Task SetAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken) { lock (_sync) _sessions[session.SearchId] = session; return Task.CompletedTask; }
        public Task<bool> TrySetUnlessCanceledAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_sessions.GetValueOrDefault(session.SearchId)?.Status == "canceled") return Task.FromResult(false);
                _sessions[session.SearchId] = session;
                return Task.FromResult(true);
            }
        }
        public Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken) { lock (_sync) return Task.FromResult(_sessions.GetValueOrDefault(searchId)); }
    }
}
