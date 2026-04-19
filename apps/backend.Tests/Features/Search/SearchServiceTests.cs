using backend.Features.Search;
using backend.Features.Search.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
            SelectedDates: [new DateOnly(2026, 5, 15)],
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
            SelectedDates: [new DateOnly(2026, 5, 15)],
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, CancellationToken.None));

        Assert.Equal("Search exceeds the limit of 60 combinations.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_RejectsRequestsWithNoValidExpandedCombinations()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var request = new SearchRequest(
            OriginAirports: ["DUB"],
            DestinationAirports: ["DUB"],
            SelectedDates: [new DateOnly(2026, 5, 15)],
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, CancellationToken.None));

        Assert.Equal("Search must contain at least one valid origin, destination, and departure date combination.", error.Message);
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

        Assert.Equal(DateTimeKind.Unspecified, leg.DepartureLocalTime.Kind);
        Assert.Equal(DateTimeKind.Unspecified, leg.ArrivalLocalTime.Kind);
        Assert.Equal(new DateTime(2026, 5, 15, 6, 30, 0), leg.DepartureLocalTime);
        Assert.Equal(new DateTime(2026, 5, 15, 9, 15, 0), leg.ArrivalLocalTime);
        Assert.Equal(DateTimeKind.Unspecified, segment.DepartureLocalTime.Kind);
        Assert.Equal(DateTimeKind.Unspecified, segment.ArrivalLocalTime.Kind);
        Assert.Equal(new DateTime(2026, 5, 15, 6, 30, 0), segment.DepartureLocalTime);
        Assert.Equal(new DateTime(2026, 5, 15, 9, 15, 0), segment.ArrivalLocalTime);
    }

    [Fact]
    public async Task StartSearchAsync_PreservesPartialResults_WhenUnexpectedBackgroundErrorOccurs()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new TimedFlightSearchProvider();
        var scopeFactory = new ThrowOnSecondScopeFactory(provider);
        var service = new SearchService(scopeFactory, store, NullLogger<SearchService>.Instance);

        var request = new SearchRequest(
            ["DUB"],
            ["AMS", "CDG"],
            [new DateOnly(2026, 5, 15)],
            null,
            null,
            1,
            "economy");

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("failed", finalSession.Status);
        Assert.Equal(1, finalSession.CompletedCombinations);
        Assert.Equal(1, finalSession.FailedCombinations);
        Assert.Equal("Search failed before completion.", finalSession.ErrorMessage);
        Assert.Single(finalSession.Response.Results);
    }

    [Fact]
    public async Task StartSearchAsync_ExpandsRoundTripSearchIntoOutboundInboundAndRoundTripCalls()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 15)],
            new DateOnly(2026, 5, 15),
            new DateOnly(2026, 5, 15),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new RecordingFlightSearchProvider();
        var service = CreateSearchService(store, provider);

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Single(provider.Requests, item => item.OriginAirport == "DUB" && item.DestinationAirport == "AMS");
        Assert.Single(provider.Requests, item => item.OriginAirport == "AMS" && item.DestinationAirport == "DUB");
        Assert.Single(provider.RoundTripRequests);
    }

    [Fact]
    public async Task StartSearchAsync_MergesSyntheticAndActualRoundTripFares_AndKeepsSyntheticBookingLinks()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 15)],
            new DateOnly(2026, 5, 15),
            new DateOnly(2026, 5, 15),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new RoundTripMergeFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(3, finalSession.TotalCombinations);
        Assert.Equal(2, finalSession.Response.Results.Count);
        Assert.All(finalSession.Response.Results, result => Assert.True(result.IsRoundTrip));

        var cheapestResult = finalSession.Response.Results[0];
        Assert.Equal(170m, cheapestResult.PriceOptions[0].TotalPrice.Amount);
        Assert.Single(cheapestResult.PriceOptions[0].BookingLinks);

        var syntheticResult = Assert.Single(finalSession.Response.Results,
            result => result.PriceOptions[0].Provider.Contains("Combined one-way", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(190m, syntheticResult.PriceOptions[0].TotalPrice.Amount);
        Assert.Equal(2, syntheticResult.PriceOptions[0].BookingLinks.Count);
        Assert.Equal("Book outbound", syntheticResult.PriceOptions[0].BookingLinks[0].Label);
        Assert.Equal("Book return", syntheticResult.PriceOptions[0].BookingLinks[1].Label);
        Assert.Equal(110m, syntheticResult.PriceOptions[0].BookingLinks[0].Price?.Amount);
        Assert.Equal(80m, syntheticResult.PriceOptions[0].BookingLinks[1].Price?.Amount);
    }

    [Fact]
    public async Task StartSearchAsync_DropsSyntheticRoundTripFares_WhenEitherHalfLacksABookingLink()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 15)],
            new DateOnly(2026, 5, 15),
            new DateOnly(2026, 5, 15),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new MissingSyntheticBookingLinkFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.DoesNotContain(
            finalSession.Response.Results.SelectMany(result => result.PriceOptions),
            option => option.Provider.Contains("Combined one-way", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StartSearchAsync_BuildsMixedDestinationSyntheticReturns_ForSelectedDestinationSet()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS", "DUS"],
            [new DateOnly(2026, 5, 15)],
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 16),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new OpenJawSyntheticFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(request, CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(6, finalSession.TotalCombinations);
        Assert.Equal(4, finalSession.Response.Results.Count);
        Assert.All(finalSession.Response.Results, result => Assert.True(result.IsRoundTrip));

        var routePairs = finalSession.Response.Results
            .Select(result =>
            {
                var outbound = result.Legs[0];
                var inbound = result.Legs[1];
                return $"{outbound.OriginAirport}-{outbound.DestinationAirport}|{inbound.OriginAirport}-{inbound.DestinationAirport}";
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "DUB-AMS|AMS-DUB",
                "DUB-AMS|DUS-DUB",
                "DUB-DUS|AMS-DUB",
                "DUB-DUS|DUS-DUB"
            ],
            routePairs);
    }

    [Fact]
    public async Task GetSearchAsync_AppliesBackendFilters_AndReturnsFilterMetadata()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(CreateStoredSession(), CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var response = await service.GetSearchAsync(
            "search-1",
            new SearchResultsQuery
            {
                Direct = true,
                OneStop = false,
                TwoPlusStop = false,
                Providers = "FlightApi:KLM",
                DepartureTime = "0-720",
                MaxDuration = 120
            },
            CancellationToken.None);

        Assert.NotNull(response);
        var session = response!;
        var result = Assert.Single(session.Response.Results);
        Assert.Equal("result-1", result.Id);
        Assert.Equal(1, session.Response.Metadata.ProviderResultCount);
        Assert.Equal(1, session.Response.Metadata.ReturnedResultCount);
        Assert.Contains(session.Response.Filters.Providers, option => option.Value == "FlightApi:Ryanair" && option.Count == 1);
        Assert.Equal(100, session.Response.Filters.DurationMinutes.Min);
        Assert.Equal(100, session.Response.Filters.DurationMinutes.Max);
    }

    [Fact]
    public async Task GetSearchAsync_AppliesPagination_WhenRequested()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(CreateStoredSession(), CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var response = await service.GetSearchAsync(
            "search-1",
            new SearchResultsQuery
            {
                Page = 2,
                PageSize = 1
            },
            CancellationToken.None);

        Assert.NotNull(response);
        var session = response!;
        var result = Assert.Single(session.Response.Results);
        Assert.Equal("result-2", result.Id);
        Assert.Equal(2, session.Response.Pagination.Page);
        Assert.Equal(1, session.Response.Pagination.PageSize);
        Assert.Equal(3, session.Response.Pagination.TotalResults);
        Assert.Equal(3, session.Response.Pagination.TotalPages);
    }

    [Fact]
    public async Task GetSearchAsync_PaginatesBeyondFormerCanonicalCap()
    {
        var results = Enumerable.Range(1, 2050)
            .Select(index => CreateResult(
                $"result-{index}",
                "DUB",
                "AMS",
                new DateTime(2026, 5, 15, 8, 0, 0).AddMinutes(index),
                new DateTime(2026, 5, 15, 9, 40, 0).AddMinutes(index),
                100,
                [
                    CreateSegment("KLM", "KL", $"F{index}", "DUB", "AMS", new DateTime(2026, 5, 15, 8, 0, 0).AddMinutes(index), new DateTime(2026, 5, 15, 9, 40, 0).AddMinutes(index), 100)
                ],
                [
                    CreatePriceOption($"p{index}", "FlightApi:KLM", 100m + index)
                ]))
            .ToList();

        var session = new SearchSessionResponse(
            "search-many",
            "completed",
            2050,
            2050,
            0,
            new SearchResponse(
                results,
                new SearchMetadata(2050, 2050, 2050, 2050, 0, 0),
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 2050, 2050, 1)),
            null);

        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(session, CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var response = await service.GetSearchAsync(
            "search-many",
            new SearchResultsQuery
            {
                Page = 21,
                PageSize = 100
            },
            CancellationToken.None);

        Assert.NotNull(response);
        var pagedSession = response!;
        Assert.Equal(50, pagedSession.Response.Results.Count);
        Assert.Equal("result-2001", pagedSession.Response.Results[0].Id);
        Assert.Equal("result-2050", pagedSession.Response.Results[^1].Id);
        Assert.Equal(21, pagedSession.Response.Pagination.Page);
        Assert.Equal(100, pagedSession.Response.Pagination.PageSize);
        Assert.Equal(2050, pagedSession.Response.Pagination.TotalResults);
        Assert.Equal(21, pagedSession.Response.Pagination.TotalPages);
    }

    private static SearchRequest CreateRequest() =>
        new(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 15)],
            null,
            null,
            1,
            "economy");

    private static SearchSessionResponse CreateStoredSession() =>
        new(
            "search-1",
            "completed",
            3,
            3,
            0,
            new SearchResponse(
                [
                    CreateResult(
                        "result-1",
                        "DUB",
                        "AMS",
                        new DateTime(2026, 5, 15, 8, 0, 0),
                        new DateTime(2026, 5, 15, 9, 40, 0),
                        100,
                        [
                            CreateSegment("KLM", "KL", "100", "DUB", "AMS", new DateTime(2026, 5, 15, 8, 0, 0), new DateTime(2026, 5, 15, 9, 40, 0), 100)
                        ],
                        [
                            CreatePriceOption("p1", "FlightApi:KLM", 120m),
                            CreatePriceOption("p2", "FlightApi:Expedia", 125m)
                        ]),
                    CreateResult(
                        "result-2",
                        "DUB",
                        "AMS",
                        new DateTime(2026, 5, 15, 11, 0, 0),
                        new DateTime(2026, 5, 15, 15, 0, 0),
                        240,
                        [
                            CreateSegment("Air France", "AF", "200", "DUB", "CDG", new DateTime(2026, 5, 15, 11, 0, 0), new DateTime(2026, 5, 15, 13, 0, 0), 120),
                            CreateSegment("Air France", "AF", "201", "CDG", "AMS", new DateTime(2026, 5, 15, 14, 0, 0), new DateTime(2026, 5, 15, 15, 0, 0), 60)
                        ],
                        [
                            CreatePriceOption("p3", "FlightApi:Air France", 95m)
                        ]),
                    CreateResult(
                        "result-3",
                        "DUB",
                        "AMS",
                        new DateTime(2026, 5, 15, 18, 0, 0),
                        new DateTime(2026, 5, 15, 19, 40, 0),
                        100,
                        [
                            CreateSegment("Ryanair", "FR", "300", "DUB", "AMS", new DateTime(2026, 5, 15, 18, 0, 0), new DateTime(2026, 5, 15, 19, 40, 0), 100)
                        ],
                        [
                            CreatePriceOption("p4", "FlightApi:Ryanair", 60m)
                        ])
                ],
                new SearchMetadata(3, 4, 3, 2, 1, 0),
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 3, 3, 1)),
            null);

    private static SearchResult CreateResult(
        string id,
        string originAirport,
        string destinationAirport,
        DateTime departureLocalTime,
        DateTime arrivalLocalTime,
        int totalDurationMinutes,
        List<SearchResultSegment> segments,
        List<SearchResultPriceOption> priceOptions) =>
        new(
            id,
            false,
            [
                new SearchResultLeg(
                    originAirport,
                    destinationAirport,
                    departureLocalTime,
                    arrivalLocalTime,
                    totalDurationMinutes,
                    segments)
            ],
            totalDurationMinutes,
            priceOptions);

    private static SearchResultSegment CreateSegment(
        string carrierName,
        string carrierCode,
        string flightNumber,
        string originAirport,
        string destinationAirport,
        DateTime departureLocalTime,
        DateTime arrivalLocalTime,
        int durationMinutes) =>
        new(
            carrierName,
            carrierCode,
            flightNumber,
            originAirport,
            destinationAirport,
            departureLocalTime,
            arrivalLocalTime,
            durationMinutes);

    private static SearchResultPriceOption CreatePriceOption(string id, string provider, decimal amount) =>
        new(
            id,
            provider,
            new SearchResultPrice(amount, "EUR"),
            [new SearchResultBookingLink("View fare", $"https://example.com/{id}")]);

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

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());
    }

    private sealed class RecordingFlightSearchProvider : IFlightSearchProvider
    {
        private readonly Lock _lock = new();
        private readonly List<ProviderSearchRequest> _requests = [];
        private readonly List<ProviderRoundTripSearchRequest> _roundTripRequests = [];

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

        public IReadOnlyList<ProviderRoundTripSearchRequest> RoundTripRequests
        {
            get
            {
                lock (_lock)
                {
                    return [.. _roundTripRequests];
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

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _roundTripRequests.Add(request);
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

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
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

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());
    }

    private sealed class OpenJawSyntheticFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            var itineraryId = $"{request.OriginAirport}-{request.DestinationAirport}-{request.DepartureDate:yyyyMMdd}";
            var legId = $"{itineraryId}-leg";
            var segmentId = $"{itineraryId}-segment";
            var departure = request.DepartureDate == new DateOnly(2026, 5, 15)
                ? new DateTime(2026, 5, 15, request.DestinationAirport == "AMS" ? 8 : 9, 0, 0)
                : new DateTime(2026, 5, 16, request.OriginAirport == "AMS" ? 12 : 13, 0, 0);
            var arrival = departure.AddHours(2);

            return Task.FromResult(new FlightApiOneWayResponse
            {
                Itineraries =
                [
                    new FlightApiItinerary
                    {
                        Id = itineraryId,
                        LegIds = [legId],
                        DeepLink = $"https://example.com/{itineraryId}",
                        PricingOptions =
                        [
                            new FlightApiPricingOption
                            {
                                Id = "pricing-1",
                                Price = new FlightApiPrice { Amount = 100m },
                                Items = []
                            }
                        ]
                    }
                ],
                Legs =
                [
                    new FlightApiLeg
                    {
                        Id = legId,
                        OriginPlaceId = request.OriginAirport == "DUB" ? 1 : request.OriginAirport == "AMS" ? 2 : 3,
                        DestinationPlaceId = request.DestinationAirport == "DUB" ? 1 : request.DestinationAirport == "AMS" ? 2 : 3,
                        Departure = departure,
                        Arrival = arrival,
                        Duration = 120,
                        SegmentIds = [segmentId]
                    }
                ],
                Segments =
                [
                    new FlightApiSegment
                    {
                        Id = segmentId,
                        OriginPlaceId = request.OriginAirport == "DUB" ? 1 : request.OriginAirport == "AMS" ? 2 : 3,
                        DestinationPlaceId = request.DestinationAirport == "DUB" ? 1 : request.DestinationAirport == "AMS" ? 2 : 3,
                        Departure = departure,
                        Arrival = arrival,
                        Duration = 120,
                        MarketingCarrierId = 1,
                        MarketingFlightNumber = "100"
                    }
                ],
                Places =
                [
                    new FlightApiPlace { Id = 1, DisplayCode = "DUB" },
                    new FlightApiPlace { Id = 2, DisplayCode = "AMS" },
                    new FlightApiPlace { Id = 3, DisplayCode = "DUS" }
                ],
                Carriers =
                [
                    new FlightApiCarrier { Id = 1, Name = "Test Airline", DisplayCode = "TA" }
                ]
            });
        }

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());
    }

    private sealed class RoundTripMergeFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OriginAirport == "DUB" && request.DestinationAirport == "AMS")
            {
                return Task.FromResult(CreateResponse(
                    CreateSingleLegItinerary("outbound-1", "pricing-outbound-1", 1, 2, new DateTime(2026, 5, 15, 8, 0, 0), new DateTime(2026, 5, 15, 10, 0, 0), 100m, "https://example.com/outbound-1"),
                    CreateSingleLegItinerary("outbound-2", "pricing-outbound-2", 1, 2, new DateTime(2026, 5, 15, 12, 0, 0), new DateTime(2026, 5, 15, 14, 0, 0), 110m, "https://example.com/outbound-2")));
            }

            return Task.FromResult(CreateResponse(
                CreateSingleLegItinerary("inbound-early", "pricing-inbound-early", 2, 1, new DateTime(2026, 5, 15, 7, 0, 0), new DateTime(2026, 5, 15, 9, 0, 0), 70m, "https://example.com/inbound-early"),
                CreateSingleLegItinerary("inbound-late", "pricing-inbound-late", 2, 1, new DateTime(2026, 5, 15, 20, 0, 0), new DateTime(2026, 5, 15, 22, 0, 0), 80m, "https://example.com/inbound-late")));
        }

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateResponse(
                CreateRoundTripItinerary(
                    "actual-roundtrip",
                    "pricing-actual-roundtrip",
                    new DateTime(2026, 5, 15, 8, 0, 0),
                    new DateTime(2026, 5, 15, 10, 0, 0),
                    new DateTime(2026, 5, 15, 20, 0, 0),
                    new DateTime(2026, 5, 15, 22, 0, 0),
                    170m,
                    "https://example.com/actual-roundtrip")));

        private static FlightApiOneWayResponse CreateResponse(params FlightApiTestItinerary[] itineraries) =>
            new()
            {
                Itineraries = itineraries.Select(item => item.Itinerary).ToList(),
                Legs = itineraries.SelectMany(item => item.Legs).ToList(),
                Segments = itineraries.SelectMany(item => item.Segments).ToList(),
                Places =
                [
                    new FlightApiPlace { Id = 1, Iata = "DUB", DisplayCode = "DUB", Name = "Dublin" },
                    new FlightApiPlace { Id = 2, Iata = "AMS", DisplayCode = "AMS", Name = "Amsterdam" }
                ],
                Carriers =
                [
                    new FlightApiCarrier { Id = 1, Name = "Test Airline", DisplayCode = "TA" }
                ]
            };

        private static FlightApiTestItinerary CreateSingleLegItinerary(
            string id,
            string pricingId,
            int originPlaceId,
            int destinationPlaceId,
            DateTime departure,
            DateTime arrival,
            decimal amount,
            string deepLink)
        {
            var legId = $"{id}-leg";
            var segmentId = $"{id}-segment";

            return new FlightApiTestItinerary(
                new FlightApiItinerary
                {
                    Id = id,
                    LegIds = [legId],
                    DeepLink = deepLink,
                    PricingOptions =
                    [
                        new FlightApiPricingOption
                        {
                            Id = pricingId,
                            Price = new FlightApiPrice { Amount = amount },
                            Items = []
                        }
                    ]
                },
                [
                    new FlightApiLeg
                    {
                        Id = legId,
                        OriginPlaceId = originPlaceId,
                        DestinationPlaceId = destinationPlaceId,
                        Departure = departure,
                        Arrival = arrival,
                        SegmentIds = [segmentId],
                        Duration = (int)(arrival - departure).TotalMinutes
                    }
                ],
                [
                    new FlightApiSegment
                    {
                        Id = segmentId,
                        OriginPlaceId = originPlaceId,
                        DestinationPlaceId = destinationPlaceId,
                        Departure = departure,
                        Arrival = arrival,
                        Duration = (int)(arrival - departure).TotalMinutes,
                        MarketingFlightNumber = id,
                        MarketingCarrierId = 1
                    }
                ]);
        }

        private static FlightApiTestItinerary CreateRoundTripItinerary(
            string id,
            string pricingId,
            DateTime outboundDeparture,
            DateTime outboundArrival,
            DateTime inboundDeparture,
            DateTime inboundArrival,
            decimal amount,
            string deepLink)
        {
            var outboundLegId = $"{id}-outbound-leg";
            var inboundLegId = $"{id}-inbound-leg";
            var outboundSegmentId = $"{id}-outbound-segment";
            var inboundSegmentId = $"{id}-inbound-segment";

            return new FlightApiTestItinerary(
                new FlightApiItinerary
                {
                    Id = id,
                    LegIds = [outboundLegId, inboundLegId],
                    DeepLink = deepLink,
                    PricingOptions =
                    [
                        new FlightApiPricingOption
                        {
                            Id = pricingId,
                            Price = new FlightApiPrice { Amount = amount },
                            Items = []
                        }
                    ]
                },
                [
                    new FlightApiLeg
                    {
                        Id = outboundLegId,
                        OriginPlaceId = 1,
                        DestinationPlaceId = 2,
                        Departure = outboundDeparture,
                        Arrival = outboundArrival,
                        SegmentIds = [outboundSegmentId],
                        Duration = (int)(outboundArrival - outboundDeparture).TotalMinutes
                    },
                    new FlightApiLeg
                    {
                        Id = inboundLegId,
                        OriginPlaceId = 2,
                        DestinationPlaceId = 1,
                        Departure = inboundDeparture,
                        Arrival = inboundArrival,
                        SegmentIds = [inboundSegmentId],
                        Duration = (int)(inboundArrival - inboundDeparture).TotalMinutes
                    }
                ],
                [
                    new FlightApiSegment
                    {
                        Id = outboundSegmentId,
                        OriginPlaceId = 1,
                        DestinationPlaceId = 2,
                        Departure = outboundDeparture,
                        Arrival = outboundArrival,
                        Duration = (int)(outboundArrival - outboundDeparture).TotalMinutes,
                        MarketingFlightNumber = "outbound-1",
                        MarketingCarrierId = 1
                    },
                    new FlightApiSegment
                    {
                        Id = inboundSegmentId,
                        OriginPlaceId = 2,
                        DestinationPlaceId = 1,
                        Departure = inboundDeparture,
                        Arrival = inboundArrival,
                        Duration = (int)(inboundArrival - inboundDeparture).TotalMinutes,
                        MarketingFlightNumber = "inbound-late",
                        MarketingCarrierId = 1
                    }
                ]);
        }

        private sealed record FlightApiTestItinerary(
            FlightApiItinerary Itinerary,
            List<FlightApiLeg> Legs,
            List<FlightApiSegment> Segments);
    }

    private sealed class MissingSyntheticBookingLinkFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OriginAirport == "DUB" && request.DestinationAirport == "AMS")
            {
                return Task.FromResult(CreateResponse(
                    CreateSingleLegItinerary(
                        "outbound-missing-link",
                        "pricing-outbound-missing-link",
                        1,
                        2,
                        new DateTime(2026, 5, 15, 8, 0, 0),
                        new DateTime(2026, 5, 15, 10, 0, 0),
                        100m,
                        string.Empty)));
            }

            return Task.FromResult(CreateResponse(
                CreateSingleLegItinerary(
                    "inbound-valid-link",
                    "pricing-inbound-valid-link",
                    2,
                    1,
                    new DateTime(2026, 5, 15, 20, 0, 0),
                    new DateTime(2026, 5, 15, 22, 0, 0),
                    80m,
                    "https://example.com/inbound-valid-link")));
        }

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());

        private static FlightApiOneWayResponse CreateResponse(params FlightApiTestItinerary[] itineraries) =>
            new()
            {
                Itineraries = itineraries.Select(item => item.Itinerary).ToList(),
                Legs = itineraries.SelectMany(item => item.Legs).ToList(),
                Segments = itineraries.SelectMany(item => item.Segments).ToList(),
                Places =
                [
                    new FlightApiPlace { Id = 1, Iata = "DUB", DisplayCode = "DUB", Name = "Dublin" },
                    new FlightApiPlace { Id = 2, Iata = "AMS", DisplayCode = "AMS", Name = "Amsterdam" }
                ],
                Carriers =
                [
                    new FlightApiCarrier { Id = 1, Name = "Test Airline", DisplayCode = "TA" }
                ]
            };

        private static FlightApiTestItinerary CreateSingleLegItinerary(
            string id,
            string pricingId,
            int originPlaceId,
            int destinationPlaceId,
            DateTime departure,
            DateTime arrival,
            decimal amount,
            string deepLink)
        {
            var legId = $"{id}-leg";
            var segmentId = $"{id}-segment";

            return new FlightApiTestItinerary(
                new FlightApiItinerary
                {
                    Id = id,
                    LegIds = [legId],
                    DeepLink = deepLink,
                    PricingOptions =
                    [
                        new FlightApiPricingOption
                        {
                            Id = pricingId,
                            Price = new FlightApiPrice { Amount = amount },
                            Items = []
                        }
                    ]
                },
                [
                    new FlightApiLeg
                    {
                        Id = legId,
                        OriginPlaceId = originPlaceId,
                        DestinationPlaceId = destinationPlaceId,
                        Departure = departure,
                        Arrival = arrival,
                        SegmentIds = [segmentId],
                        Duration = (int)(arrival - departure).TotalMinutes
                    }
                ],
                [
                    new FlightApiSegment
                    {
                        Id = segmentId,
                        OriginPlaceId = originPlaceId,
                        DestinationPlaceId = destinationPlaceId,
                        Departure = departure,
                        Arrival = arrival,
                        Duration = (int)(arrival - departure).TotalMinutes,
                        MarketingFlightNumber = id,
                        MarketingCarrierId = 1
                    }
                ]);
        }

        private sealed record FlightApiTestItinerary(
            FlightApiItinerary Itinerary,
            List<FlightApiLeg> Legs,
            List<FlightApiSegment> Segments);
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

    private sealed class ThrowOnSecondScopeFactory(IFlightSearchProvider provider) : IServiceScopeFactory
    {
        private int _callCount;

        public IServiceScope CreateScope()
        {
            _callCount += 1;
            if (_callCount == 2)
            {
                throw new InvalidOperationException("Injected scope creation failure.");
            }

            return new TestServiceScope(provider);
        }
    }

    private sealed class TestServiceScope(IFlightSearchProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(provider);

        public void Dispose()
        {
        }
    }

    private sealed class TestServiceProvider(IFlightSearchProvider provider) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IFlightSearchProvider) ? provider : null;
    }
}
