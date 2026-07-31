using backend.Features.Search;
using backend.Features.Search.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class SearchServiceTests
{
    [Fact]
    public async Task StartSearchAsync_Completes_WhenIntermediateSessionPersistenceFails()
    {
        var store = new FlakySearchSessionStore(failingCalls: [2]);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(CreateRequest(), new SearchLimit(100, null), CancellationToken.None);

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

        var initialSession = await service.StartSearchAsync(CreateRequest(), new SearchLimit(100, null), CancellationToken.None);

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

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
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
            OriginAirports: ["DUB", "ORK", "SNN", "NOC", "KIR", "WAT", "SXL", "CFN", "KNO"],
            DestinationAirports: ["AMS", "BCN", "CDG", "FRA", "MAD", "LHR", "OSL", "CPH", "MXP", "ARN", "HEL", "BER"],
            SelectedDates: [new DateOnly(2026, 5, 15)],
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None));

        Assert.Equal("Search exceeds the limit of 100 combinations.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_RejectsHugeReturnRange_BeforeMaterializingIt()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new RecordingFlightSearchProvider();
        var service = CreateSearchService(store, provider);
        var request = new SearchRequest(
            OriginAirports: ["DUB"],
            DestinationAirports: ["AMS"],
            SelectedDates: [DateOnly.MinValue],
            ReturnDateFrom: DateOnly.MinValue,
            ReturnDateTo: DateOnly.MaxValue,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None));

        Assert.Equal("Search exceeds the limit of 100 combinations.", error.Message);
        Assert.Empty(provider.Requests);
        Assert.Empty(provider.RoundTripRequests);
    }

    [Fact]
    public async Task StartSearchAsync_RejectsReturnRangeBeforeLatestOutboundDate()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new RecordingFlightSearchProvider();
        var service = CreateSearchService(store, provider);
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 9)],
            new DateOnly(2026, 8, 7),
            new DateOnly(2026, 8, 7),
            1,
            "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None));

        Assert.Equal(
            "The return date range must start on or after the latest outbound date.",
            error.Message);
        Assert.Empty(provider.Requests);
        Assert.Empty(provider.RoundTripRequests);
    }

    [Fact]
    public async Task StartSearchAsync_UsesProvidedSearchCombinationLimit()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var request = new SearchRequest(
            OriginAirports: ["DUB"],
            DestinationAirports: ["AMS"],
            SelectedDates: [new DateOnly(2026, 5, 15)],
            ReturnDateFrom: new DateOnly(2026, 5, 15),
            ReturnDateTo: new DateOnly(2026, 5, 15),
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, new SearchLimit(2, null), CancellationToken.None));

        Assert.Equal("Search exceeds the limit of 2 combinations.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_UsesProvidedLimitExceededMessage()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var request = new SearchRequest(
            OriginAirports: ["DUB"],
            DestinationAirports: ["AMS"],
            SelectedDates: [new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 16)],
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartSearchAsync(
                request,
                new SearchLimit(1, "Sign up or log in to search up to 100 combinations."),
                CancellationToken.None));

        Assert.Equal("Sign up or log in to search up to 100 combinations.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_AllowsUnlimitedSearchCombinationLimit()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new RecordingFlightSearchProvider();
        var service = CreateSearchService(store, provider);

        var request = new SearchRequest(
            OriginAirports: ["DUB", "ORK", "SNN"],
            DestinationAirports: ["AMS", "BCN", "CDG", "FRA", "MAD", "LHR"],
            SelectedDates: [new DateOnly(2026, 5, 15)],
            ReturnDateFrom: null,
            ReturnDateTo: null,
            Adults: 1,
            CabinClass: "economy");

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(null, null), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(18, provider.Requests.Count);
    }

    [Fact]
    public async Task StartSearchAsync_CapsStoredFareOptionsFromLargeProviderResponses()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new LargeResultFlightSearchProvider(2001));

        var initialSession = await service.StartSearchAsync(
            CreateRequest(),
            new SearchLimit(100, null),
            CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(2000, finalSession.Response.Results.Count);
        Assert.Equal(2000, finalSession.Response.Metadata.ProviderResultCount);
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

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None));

        Assert.Equal("Search must contain at least one valid origin, destination, and departure date combination.", error.Message);
    }

    [Fact]
    public async Task StartSearchAsync_PreservesProviderLocalTimes_WithoutForcingUtc()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new TimedFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(CreateRequest(), new SearchLimit(100, null), CancellationToken.None);
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
        var service = new SearchService(
            scopeFactory,
            store,
            NullLogger<SearchService>.Instance);

        var request = new SearchRequest(
            ["DUB"],
            ["AMS", "CDG"],
            [new DateOnly(2026, 5, 15)],
            null,
            null,
            1,
            "economy");

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
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

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Single(provider.Requests, item => item.OriginAirport == "DUB" && item.DestinationAirport == "AMS");
        Assert.Single(provider.Requests, item => item.OriginAirport == "AMS" && item.DestinationAirport == "DUB");
        Assert.Single(provider.RoundTripRequests);
    }

    [Fact]
    public async Task StartSearchAsync_NormalizesDuplicateProviderEntitiesWithoutFailingCombination()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["CGN"],
            [new DateOnly(2026, 8, 7)],
            new DateOnly(2026, 8, 12),
            new DateOnly(2026, 8, 12),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var service = CreateSearchService(store, new DuplicateEntityFlightSearchProvider());

        var initialSession = await service.StartSearchAsync(
            request,
            new SearchLimit(100, null),
            CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(0, finalSession.FailedCombinations);
        Assert.NotNull(finalSession.StagedResults);
        Assert.Single(finalSession.StagedResults!.RoundTripResults);
    }

    [Fact]
    public async Task StartSearchAsync_PrefetchesReturnsWhileOutboundPhaseIsStillRunning()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 15)],
            new DateOnly(2026, 5, 16),
            new DateOnly(2026, 5, 16),
            1,
            "economy");
        var store = new FlakySearchSessionStore(failingCalls: []);
        var provider = new PhasedFlightSearchProvider();
        var service = CreateSearchService(store, provider);

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
        await provider.OutboundStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await provider.ReturnPhaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(provider.ReturnCalls > 0);

        provider.ReleaseOutbound.TrySetResult();
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal(2, provider.ReturnCalls);
        Assert.Equal("completed", finalSession.Status);
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

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(3, finalSession.TotalCombinations);
        Assert.Equal(2, finalSession.Response.Results.Count);
        Assert.All(finalSession.Response.Results, result => Assert.False(result.IsRoundTrip));
        Assert.NotNull(finalSession.StagedResults);
        Assert.Equal(2, finalSession.StagedResults!.OutboundResults.Count);
        Assert.Equal(2, finalSession.StagedResults.InboundResults.Count);
        Assert.Single(finalSession.StagedResults.RoundTripResults);

        var selectedOutboundLegId = finalSession.Response.Results[0].Legs[0].Id;
        var selectedSession = await service.GetSearchAsync(
            initialSession.SearchId,
            new SearchResultsQuery { OutboundLegId = selectedOutboundLegId },
            CancellationToken.None);

        Assert.NotNull(selectedSession);
        Assert.Null(selectedSession!.StagedResults);
        Assert.Equal(2, selectedSession.Response.Results.Count);
        var actualResult = Assert.Single(selectedSession.Response.Results,
            result => !result.PriceOptions[0].Provider.Contains("Combined one-way", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(170m, actualResult.PriceOptions[0].TotalPrice.Amount);

        var syntheticResult = Assert.Single(selectedSession.Response.Results,
            result => result.PriceOptions[0].Provider.Contains("Combined one-way", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(180m, syntheticResult.PriceOptions[0].TotalPrice.Amount);
        Assert.Equal(2, syntheticResult.PriceOptions[0].BookingLinks.Count);
        Assert.Equal("Book outbound", syntheticResult.PriceOptions[0].BookingLinks[0].Label);
        Assert.Equal("Book return", syntheticResult.PriceOptions[0].BookingLinks[1].Label);
        Assert.Equal(100m, syntheticResult.PriceOptions[0].BookingLinks[0].Price?.Amount);
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

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        var selectedSession = await service.GetSearchAsync(
            initialSession.SearchId,
            new SearchResultsQuery { OutboundLegId = finalSession.Response.Results[0].Legs[0].Id },
            CancellationToken.None);

        Assert.NotNull(selectedSession);
        Assert.DoesNotContain(
            selectedSession!.Response.Results.SelectMany(result => result.PriceOptions),
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

        var initialSession = await service.StartSearchAsync(request, new SearchLimit(100, null), CancellationToken.None);
        var finalSession = await store.WaitForTerminalSessionAsync(initialSession.SearchId);

        Assert.Equal("completed", finalSession.Status);
        Assert.Equal(6, finalSession.TotalCombinations);
        Assert.Equal(2, finalSession.Response.Results.Count);
        Assert.All(finalSession.Response.Results, result => Assert.False(result.IsRoundTrip));

        var selectedSession = await service.GetSearchAsync(
            initialSession.SearchId,
            new SearchResultsQuery { OutboundLegId = finalSession.Response.Results[0].Legs[0].Id },
            CancellationToken.None);

        Assert.NotNull(selectedSession);
        var routePairs = selectedSession!.Response.Results
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
                "DUB-AMS|DUS-DUB"
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
    public async Task GetSearchAsync_AppliesReturnLegTimeFilters_AndReturnsReturnLegMetadata()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(CreateStoredRoundTripSession(), CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var response = await service.GetSearchAsync(
            "search-roundtrip",
            new SearchResultsQuery
            {
                DepartureTime = "480-600",
                ArrivalTime = "600-720",
                ReturnDepartureTime = "1080-1260",
                ReturnArrivalTime = "1200-1380"
            },
            CancellationToken.None);

        Assert.NotNull(response);
        var session = response!;
        var result = Assert.Single(session.Response.Results);
        Assert.Equal("roundtrip-1", result.Id);
        Assert.Equal(480, session.Response.Filters.DepartureTimeMinutes.Min);
        Assert.Equal(660, session.Response.Filters.ArrivalTimeMinutes.Max);
        Assert.Equal(1140, session.Response.Filters.ReturnDepartureTimeMinutes.Min);
        Assert.Equal(1320, session.Response.Filters.ReturnArrivalTimeMinutes.Max);
    }

    [Fact]
    public async Task GetSearchAsync_UsesOutboundDestinationForRoundTripArrivalFilter()
    {
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(CreateStoredRoundTripSession(), CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var response = await service.GetSearchAsync(
            "search-roundtrip",
            new SearchResultsQuery { ArrivalAirports = "AMS" },
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(2, response!.Response.Results.Count);
        Assert.Contains(
            response.Response.Filters.ArrivalAirports,
            option => option.Value == "AMS" && option.Count == 2);
        Assert.DoesNotContain(
            response.Response.Filters.ArrivalAirports,
            option => option.Value == "DUB");
    }

    [Fact]
    public async Task GetSearchAsync_FiltersRoundTripsBySelectedLeg()
    {
        var roundTripSession = CreateStoredRoundTripSession();
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(roundTripSession, CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var outboundLegId = roundTripSession.Response.Results[0].Legs[0].Id;
        var returnLegId = roundTripSession.Response.Results[1].Legs[1].Id;

        var outboundResponse = await service.GetSearchAsync(
            "search-roundtrip",
            new SearchResultsQuery
            {
                OutboundLegId = outboundLegId
            },
            CancellationToken.None);

        Assert.NotNull(outboundResponse);
        Assert.Single(outboundResponse!.Response.Results);
        Assert.Equal("roundtrip-1", outboundResponse.Response.Results[0].Id);

        var returnResponse = await service.GetSearchAsync(
            "search-roundtrip",
            new SearchResultsQuery
            {
                ReturnLegId = returnLegId
            },
            CancellationToken.None);

        Assert.NotNull(returnResponse);
        Assert.Single(returnResponse!.Response.Results);
        Assert.Equal("roundtrip-2", returnResponse.Response.Results[0].Id);
    }

    [Fact]
    public async Task GetSearchAsync_FiltersByExactLegInstance_NotJustFlightCode()
    {
        var session = CreateStoredRoundTripSessionWithRepeatedFlightNumbers();
        var store = new FlakySearchSessionStore(failingCalls: []);
        await store.SetAsync(session, CancellationToken.None);
        var service = CreateSearchService(store, new SuccessfulFlightSearchProvider());

        var firstOutboundLeg = session.Response.Results[0].Legs[0];
        var secondOutboundLeg = session.Response.Results[1].Legs[0];

        Assert.Equal(
            firstOutboundLeg.Segments[0].FlightNumber,
            secondOutboundLeg.Segments[0].FlightNumber);
        Assert.NotEqual(firstOutboundLeg.Id, secondOutboundLeg.Id);

        var response = await service.GetSearchAsync(
            "search-roundtrip-repeated-flight-numbers",
            new SearchResultsQuery
            {
                OutboundLegId = firstOutboundLeg.Id
            },
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Single(response!.Response.Results);
        Assert.Equal("roundtrip-a", response.Response.Results[0].Id);
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
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
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
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 3, 3, 1)),
            null);

    private static SearchSessionResponse CreateStoredRoundTripSession() =>
        new(
            "search-roundtrip",
            "completed",
            2,
            2,
            0,
            new SearchResponse(
                [
                    CreateRoundTripResult(
                        "roundtrip-1",
                        new DateTime(2026, 5, 15, 8, 0, 0),
                        new DateTime(2026, 5, 15, 11, 0, 0),
                        new DateTime(2026, 5, 18, 19, 0, 0),
                        new DateTime(2026, 5, 18, 22, 0, 0),
                        "FlightApi:KLM"),
                    CreateRoundTripResult(
                        "roundtrip-2",
                        new DateTime(2026, 5, 15, 13, 0, 0),
                        new DateTime(2026, 5, 15, 16, 0, 0),
                        new DateTime(2026, 5, 18, 7, 0, 0),
                        new DateTime(2026, 5, 18, 10, 0, 0),
                        "FlightApi:Air France")
                ],
                new SearchMetadata(2, 2, 2, 2, 0, 0),
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 2, 2, 1)),
            null);

    private static SearchSessionResponse CreateStoredRoundTripSessionWithRepeatedFlightNumbers() =>
        new(
            "search-roundtrip-repeated-flight-numbers",
            "completed",
            2,
            2,
            0,
            new SearchResponse(
                [
                    CreateRoundTripResult(
                        "roundtrip-a",
                        new DateTime(2026, 5, 5, 8, 0, 0),
                        new DateTime(2026, 5, 5, 11, 0, 0),
                        new DateTime(2026, 5, 12, 19, 0, 0),
                        new DateTime(2026, 5, 12, 22, 0, 0),
                        "FlightApi:KLM",
                        outboundFlightNumber: "1234",
                        returnFlightNumber: "5678"),
                    CreateRoundTripResult(
                        "roundtrip-b",
                        new DateTime(2026, 5, 19, 8, 0, 0),
                        new DateTime(2026, 5, 19, 11, 0, 0),
                        new DateTime(2026, 5, 26, 19, 0, 0),
                        new DateTime(2026, 5, 26, 22, 0, 0),
                        "FlightApi:KLM",
                        outboundFlightNumber: "1234",
                        returnFlightNumber: "5678")
                ],
                new SearchMetadata(2, 2, 2, 2, 0, 0),
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 2, 2, 1)),
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
                    $"{id}-leg",
                    originAirport,
                    destinationAirport,
                    departureLocalTime,
                    arrivalLocalTime,
                    totalDurationMinutes,
                    segments)
            ],
            totalDurationMinutes,
            priceOptions);

    private static SearchResult CreateRoundTripResult(
        string id,
        DateTime outboundDeparture,
        DateTime outboundArrival,
        DateTime returnDeparture,
        DateTime returnArrival,
        string provider,
        string outboundFlightNumber = "100",
        string returnFlightNumber = "200") =>
        new(
            id,
            true,
            [
                new SearchResultLeg(
                    $"{id}-outbound-leg",
                    "DUB",
                    "AMS",
                    outboundDeparture,
                    outboundArrival,
                    (int)(outboundArrival - outboundDeparture).TotalMinutes,
                    [
                        CreateSegment("KLM", "KL", outboundFlightNumber, "DUB", "AMS", outboundDeparture, outboundArrival, (int)(outboundArrival - outboundDeparture).TotalMinutes)
                    ]),
                new SearchResultLeg(
                    $"{id}-return-leg",
                    "AMS",
                    "DUB",
                    returnDeparture,
                    returnArrival,
                    (int)(returnArrival - returnDeparture).TotalMinutes,
                    [
                        CreateSegment("KLM", "KL", returnFlightNumber, "AMS", "DUB", returnDeparture, returnArrival, (int)(returnArrival - returnDeparture).TotalMinutes)
                    ])
            ],
            (int)(outboundArrival - outboundDeparture + (returnArrival - returnDeparture)).TotalMinutes,
            [
                CreatePriceOption($"{id}-price", provider, 150m)
            ]);

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

    private sealed class DuplicateEntityFlightSearchProvider : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FlightApiOneWayResponse());

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken)
        {
            var outboundDeparture = new DateTime(2026, 8, 7, 19, 35, 0);
            var outboundArrival = new DateTime(2026, 8, 7, 22, 30, 0);
            var inboundDeparture = new DateTime(2026, 8, 12, 8, 0, 0);
            var inboundArrival = new DateTime(2026, 8, 12, 10, 0, 0);
            var outboundLeg = new FlightApiLeg
            {
                Id = "duplicate-outbound-leg",
                OriginPlaceId = 1,
                DestinationPlaceId = 2,
                Departure = outboundDeparture,
                Arrival = outboundArrival,
                SegmentIds = ["duplicate-outbound-segment"],
                Duration = 175
            };
            var inboundLeg = new FlightApiLeg
            {
                Id = "duplicate-inbound-leg",
                OriginPlaceId = 2,
                DestinationPlaceId = 1,
                Departure = inboundDeparture,
                Arrival = inboundArrival,
                SegmentIds = ["duplicate-inbound-segment"],
                Duration = 120
            };
            var outboundSegment = new FlightApiSegment
            {
                Id = "duplicate-outbound-segment",
                OriginPlaceId = 1,
                DestinationPlaceId = 2,
                Departure = outboundDeparture,
                Arrival = outboundArrival,
                Duration = 175,
                MarketingCarrierId = 1,
                MarketingFlightNumber = "20"
            };
            var inboundSegment = new FlightApiSegment
            {
                Id = "duplicate-inbound-segment",
                OriginPlaceId = 2,
                DestinationPlaceId = 1,
                Departure = inboundDeparture,
                Arrival = inboundArrival,
                Duration = 120,
                MarketingCarrierId = 1,
                MarketingFlightNumber = "21"
            };
            var origin = new FlightApiPlace { Id = 1, Iata = "DUB" };
            var destination = new FlightApiPlace { Id = 2, Iata = "CGN" };
            var carrier = new FlightApiCarrier { Id = 1, Name = "Test Air", DisplayCode = "TA" };

            return Task.FromResult(new FlightApiOneWayResponse
            {
                Itineraries =
                [
                    new FlightApiItinerary
                    {
                        Id = "duplicate-itinerary",
                        LegIds = [outboundLeg.Id, inboundLeg.Id],
                        DeepLink = "https://example.com/duplicate-itinerary",
                        PricingOptions =
                        [
                            new FlightApiPricingOption
                            {
                                Id = "duplicate-price",
                                Price = new FlightApiPrice { Amount = 200m }
                            }
                        ]
                    }
                ],
                Legs = [outboundLeg, outboundLeg, inboundLeg, inboundLeg],
                Segments = [outboundSegment, outboundSegment, inboundSegment, inboundSegment],
                Places = [origin, origin, destination, destination],
                Carriers = [carrier, carrier]
            });
        }
    }

    private sealed class LargeResultFlightSearchProvider(int resultCount) : IFlightSearchProvider
    {
        public Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            var itineraries = new List<FlightApiItinerary>(resultCount);
            var legs = new List<FlightApiLeg>(resultCount);
            var segments = new List<FlightApiSegment>(resultCount);

            for (var index = 0; index < resultCount; index += 1)
            {
                var id = $"large-{index}";
                var departure = new DateTime(2026, 5, 15, 6, 0, 0).AddMinutes(index);
                var arrival = departure.AddMinutes(90);
                itineraries.Add(new FlightApiItinerary
                {
                    Id = id,
                    LegIds = [$"{id}-leg"],
                    DeepLink = $"https://example.com/{id}",
                    PricingOptions =
                    [
                        new FlightApiPricingOption
                        {
                            Id = $"{id}-price",
                            Price = new FlightApiPrice { Amount = 100m + index },
                            Items = []
                        }
                    ]
                });
                legs.Add(new FlightApiLeg
                {
                    Id = $"{id}-leg",
                    OriginPlaceId = 1,
                    DestinationPlaceId = 2,
                    Departure = departure,
                    Arrival = arrival,
                    Duration = 90,
                    SegmentIds = [$"{id}-segment"]
                });
                segments.Add(new FlightApiSegment
                {
                    Id = $"{id}-segment",
                    OriginPlaceId = 1,
                    DestinationPlaceId = 2,
                    Departure = departure,
                    Arrival = arrival,
                    Duration = 90,
                    MarketingCarrierId = 1,
                    MarketingFlightNumber = index.ToString()
                });
            }

            return Task.FromResult(new FlightApiOneWayResponse
            {
                Itineraries = itineraries,
                Legs = legs,
                Segments = segments,
                Places =
                [
                    new FlightApiPlace { Id = 1, Iata = "DUB" },
                    new FlightApiPlace { Id = 2, Iata = "AMS" }
                ],
                Carriers = [new FlightApiCarrier { Id = 1, Name = "Test Airline", DisplayCode = "TA" }]
            });
        }

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

    private sealed class PhasedFlightSearchProvider : IFlightSearchProvider
    {
        private int _returnCalls;

        public TaskCompletionSource OutboundStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseOutbound { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReturnPhaseStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReturnCalls => Volatile.Read(ref _returnCalls);

        public async Task<FlightApiOneWayResponse> SearchOneWayAsync(
            ProviderSearchRequest request,
            CancellationToken cancellationToken)
        {
            if (request.OriginAirport == "DUB")
            {
                OutboundStarted.TrySetResult();
                await ReleaseOutbound.Task;
                return new FlightApiOneWayResponse();
            }

            Interlocked.Increment(ref _returnCalls);
            ReturnPhaseStarted.TrySetResult();
            return new FlightApiOneWayResponse();
        }

        public Task<FlightApiOneWayResponse> SearchRoundTripAsync(
            ProviderRoundTripSearchRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _returnCalls);
            ReturnPhaseStarted.TrySetResult();
            return Task.FromResult(new FlightApiOneWayResponse());
        }
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
