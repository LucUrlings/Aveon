using backend.Features.Search.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace backend.Features.Search;

public sealed class SearchService(
    IServiceScopeFactory serviceScopeFactory,
    ISearchSessionStore searchSessionStore,
    ILogger<SearchService> logger,
    IOptions<SearchOptions>? options = null,
    IOptions<FlightApiOptions>? flightApiOptions = null) : ISearchService
{
    private const int MaxMaterializedProviderCalls = 10_000;
    private const int MaxStoredFareOptionsPerDirection = 2_000;
    private const string ProviderName = "FlightApi";
    private const string StatusRunning = "running";
    private const string StatusCompleted = "completed";
    private const string StatusFailed = "failed";
    private static readonly SearchResultsQuery EmptyQuery = new();
    private readonly TimeSpan _executionTimeout = TimeSpan.FromMinutes(Math.Max(options?.Value.ExecutionTimeoutMinutes ?? 10, 1));
    private readonly string _currency = flightApiOptions?.Value.Currency ?? "EUR";

    public async Task<SearchSessionResponse> StartSearchAsync(
        SearchRequest request,
        SearchLimit searchLimit,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var plannedProviderCallCount = CountProviderCalls(request);
        ValidateProviderCallCount(plannedProviderCallCount, searchLimit);
        var searchPlan = BuildSearchPlan(request, _currency);
        var searchId = Guid.NewGuid().ToString("N");

        var initialSession = new SearchSessionResponse(
            searchId,
            StatusRunning,
            searchPlan.TotalProviderCalls,
            0,
            0,
            BuildSearchResponse(searchPlan.TotalProviderCalls, []),
            null);

        await searchSessionStore.SetAsync(initialSession, cancellationToken);

        _ = Task.Run(() => RunSearchSafelyAsync(searchId, searchPlan), CancellationToken.None);

        return initialSession;
    }

    public async Task<SearchSessionResponse?> GetSearchAsync(string searchId, SearchResultsQuery query, CancellationToken cancellationToken)
    {
        var session = await searchSessionStore.GetAsync(searchId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var effectiveQuery = query ?? EmptyQuery;
        var canonicalResponse = session.StagedResults is not null && effectiveQuery.GetOutboundLegId() is not null
            ? BuildSelectedReturnResponse(
                session.Response.Metadata.SearchCombinationCount,
                session.StagedResults,
                effectiveQuery.GetOutboundLegId()!)
            : session.Response;

        return session with
        {
            Response = BuildFilteredSearchResponse(canonicalResponse, effectiveQuery),
            StagedResults = null
        };
    }

    private async Task RunSearchSafelyAsync(string searchId, SearchPlan searchPlan)
    {
        var executionState = new SearchExecutionState(searchPlan.IsRoundTripSearch);
        using var execution = new CancellationTokenSource(_executionTimeout);

        try
        {
            await RunSearchAsync(searchId, searchPlan, executionState, execution.Token);
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            var snapshot = executionState.Capture();
            var timedOutSession = BuildSessionSnapshot(
                searchId,
                StatusFailed,
                searchPlan.TotalProviderCalls,
                snapshot.CompletedCombinations,
                Math.Max(snapshot.FailedCombinations, searchPlan.TotalProviderCalls - snapshot.CompletedCombinations),
                snapshot.FareOptions,
                "Search exceeded its execution timeout.",
                snapshot.StagedFareOptions);
            await TrySetSearchSessionAsync(timedOutSession, "persisting timed-out search session");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background flight search failed for search session {SearchId}", searchId);

            var snapshot = executionState.Capture();
            var failedSession = BuildSessionSnapshot(
                searchId,
                StatusFailed,
                searchPlan.TotalProviderCalls,
                snapshot.CompletedCombinations,
                Math.Max(snapshot.FailedCombinations, searchPlan.TotalProviderCalls - snapshot.CompletedCombinations),
                snapshot.FareOptions,
                "Search failed before completion.",
                snapshot.StagedFareOptions);

            await TrySetSearchSessionAsync(
                failedSession,
                "persisting failed search session after an unexpected background error");
        }
    }

    private async Task RunSearchAsync(
        string searchId,
        SearchPlan searchPlan,
        SearchExecutionState executionState,
        CancellationToken cancellationToken)
    {
        var outboundTasks = searchPlan.OutboundRequests.Select(candidate =>
            ExecuteOneWaySearchAsync(
                searchId,
                searchPlan.TotalProviderCalls,
                candidate,
                searchPlan.IsRoundTripSearch ? SearchLegDirection.Outbound : SearchLegDirection.OneWay,
                executionState,
                cancellationToken))
            .ToList();

        var returnTasks = new List<Task>();
        returnTasks.AddRange(searchPlan.InboundRequests.Select(candidate =>
            ExecuteOneWaySearchAsync(
                searchId,
                searchPlan.TotalProviderCalls,
                candidate,
                SearchLegDirection.Inbound,
                executionState,
                cancellationToken)));
        returnTasks.AddRange(searchPlan.RoundTripRequests.Select(candidate =>
            ExecuteRoundTripSearchAsync(
                searchId,
                searchPlan.TotalProviderCalls,
                candidate,
                executionState,
                cancellationToken)));

        await Task.WhenAll(outboundTasks.Concat(returnTasks));

        var finalSession = executionState.BuildFinalSnapshot(searchId, searchPlan.TotalProviderCalls);

        await TrySetSearchSessionAsync(finalSession, "persisting the final search session");
    }

    private async Task ExecuteOneWaySearchAsync(
        string searchId,
        int totalProviderCalls,
        ProviderSearchRequest request,
        SearchLegDirection direction,
        SearchExecutionState executionState,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var flightSearchProvider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();

        try
        {
            var providerResponse = await flightSearchProvider.SearchOneWayAsync(request, cancellationToken);
            var mappedFareOptions = MapToSearchFareOptions(providerResponse, isRoundTrip: false)
                .Take(MaxStoredFareOptionsPerDirection)
                .ToList();

            var snapshot = executionState.BuildRunningSnapshot(searchId, totalProviderCalls, mappedFareOptions, direction);
            await TrySetSearchSessionAsync(snapshot, "persisting an in-progress search session update");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "FlightApi one-way search failed for {OriginAirport} -> {DestinationAirport} on {DepartureDate}",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate);

            var snapshot = executionState.BuildFailedProviderSnapshot(searchId, totalProviderCalls);
            await TrySetSearchSessionAsync(snapshot, "persisting a failed provider search session update");
        }
    }

    private async Task ExecuteRoundTripSearchAsync(
        string searchId,
        int totalProviderCalls,
        ProviderRoundTripSearchRequest request,
        SearchExecutionState executionState,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var flightSearchProvider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();

        try
        {
            var providerResponse = await flightSearchProvider.SearchRoundTripAsync(request, cancellationToken);
            var mappedFareOptions = MapToSearchFareOptions(providerResponse, isRoundTrip: true)
                .Take(MaxStoredFareOptionsPerDirection)
                .ToList();

            var snapshot = executionState.BuildRunningSnapshot(
                searchId,
                totalProviderCalls,
                mappedFareOptions,
                SearchLegDirection.RoundTrip);
            await TrySetSearchSessionAsync(snapshot, "persisting an in-progress search session update");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "FlightApi round-trip search failed for {OriginAirport} -> {DestinationAirport} on {DepartureDate} returning {ReturnDate}",
                request.OriginAirport,
                request.DestinationAirport,
                request.DepartureDate,
                request.ReturnDate);

            var snapshot = executionState.BuildFailedProviderSnapshot(searchId, totalProviderCalls);
            await TrySetSearchSessionAsync(snapshot, "persisting a failed provider search session update");
        }
    }

    private async Task TrySetSearchSessionAsync(SearchSessionResponse session, string operation)
    {
        try
        {
            await searchSessionStore.SetAsync(session, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Search session persistence failed while {Operation} for {SearchId}",
                operation,
                session.SearchId);
        }
    }

    private sealed class SearchExecutionState(bool isRoundTripSearch)
    {
        private readonly Lock _lock = new();
        private readonly List<SearchFareOption> _fareOptions = [];
        private readonly List<SearchFareOption> _outboundFareOptions = [];
        private readonly List<SearchFareOption> _inboundFareOptions = [];
        private readonly List<SearchFareOption> _actualRoundTripFareOptions = [];
        private int _completedCombinations;
        private int _failedCombinations;

        public SearchSessionResponse BuildRunningSnapshot(
            string searchId,
            int totalCombinations,
            IReadOnlyCollection<SearchFareOption> newFareOptions,
            SearchLegDirection direction)
        {
            lock (_lock)
            {
                switch (direction)
                {
                    case SearchLegDirection.OneWay:
                        AddBounded(_fareOptions, newFareOptions);
                        break;
                    case SearchLegDirection.Outbound:
                        AddBounded(_outboundFareOptions, newFareOptions);
                        break;
                    case SearchLegDirection.Inbound:
                        AddBounded(_inboundFareOptions, newFareOptions);
                        break;
                    case SearchLegDirection.RoundTrip:
                        AddBounded(_actualRoundTripFareOptions, newFareOptions);
                        break;
                }

                _completedCombinations += 1;
                return BuildSearchSession(searchId, StatusRunning, totalCombinations, null);
            }
        }

        public SearchSessionResponse BuildFailedProviderSnapshot(string searchId, int totalCombinations)
        {
            lock (_lock)
            {
                _completedCombinations += 1;
                _failedCombinations += 1;
                return BuildSearchSession(searchId, StatusRunning, totalCombinations, null);
            }
        }

        public SearchSessionResponse BuildFinalSnapshot(string searchId, int totalCombinations)
        {
            lock (_lock)
            {
                var status = _completedCombinations == _failedCombinations ? StatusFailed : StatusCompleted;
                var errorMessage = status == StatusFailed
                    ? "No results could be retrieved from the flight provider."
                    : null;

                return BuildSearchSession(searchId, status, totalCombinations, errorMessage);
            }
        }

        public SearchExecutionSnapshot Capture()
        {
            lock (_lock)
            {
                return new SearchExecutionSnapshot(
                    BuildCanonicalFareOptions(),
                    BuildStagedFareOptions(),
                    _completedCombinations,
                    _failedCombinations);
            }
        }

        private SearchSessionResponse BuildSearchSession(
            string searchId,
            string status,
            int totalCombinations,
            string? errorMessage) =>
            BuildSessionSnapshot(
                searchId,
                status,
                totalCombinations,
                _completedCombinations,
                _failedCombinations,
                BuildCanonicalFareOptions(),
                errorMessage,
                BuildStagedFareOptions());

        private List<SearchFareOption> BuildCanonicalFareOptions()
        {
            if (!isRoundTripSearch)
            {
                return [.. _fareOptions];
            }

            return [.. _outboundFareOptions];
        }

        private StagedFareOptions? BuildStagedFareOptions() =>
            !isRoundTripSearch
                ? null
                : new StagedFareOptions(
                    [.. _outboundFareOptions],
                    [.. _inboundFareOptions],
                    [.. _actualRoundTripFareOptions]);

        private static void AddBounded(
            List<SearchFareOption> target,
            IReadOnlyCollection<SearchFareOption> additions)
        {
            target.AddRange(additions);
            if (target.Count <= MaxStoredFareOptionsPerDirection)
            {
                return;
            }

            target.Sort((left, right) =>
            {
                var priceComparison = left.TotalPrice.Amount.CompareTo(right.TotalPrice.Amount);
                return priceComparison != 0
                    ? priceComparison
                    : left.TotalDurationMinutes.CompareTo(right.TotalDurationMinutes);
            });
            target.RemoveRange(
                MaxStoredFareOptionsPerDirection,
                target.Count - MaxStoredFareOptionsPerDirection);
        }
    }

    private sealed record SearchExecutionSnapshot(
        List<SearchFareOption> FareOptions,
        StagedFareOptions? StagedFareOptions,
        int CompletedCombinations,
        int FailedCombinations);

    private sealed record StagedFareOptions(
        List<SearchFareOption> OutboundFareOptions,
        List<SearchFareOption> InboundFareOptions,
        List<SearchFareOption> RoundTripFareOptions);

    private sealed record SearchPlan(
        bool IsRoundTripSearch,
        List<ProviderSearchRequest> OutboundRequests,
        List<ProviderSearchRequest> InboundRequests,
        List<ProviderRoundTripSearchRequest> RoundTripRequests)
    {
        public int TotalProviderCalls => OutboundRequests.Count + InboundRequests.Count + RoundTripRequests.Count;
    }

    private enum SearchLegDirection
    {
        OneWay,
        Outbound,
        Inbound,
        RoundTrip
    }

    private static SearchSessionResponse BuildSessionSnapshot(
        string searchId,
        string status,
        int totalCombinations,
        int completedCombinations,
        int failedCombinations,
        List<SearchFareOption> fareOptions,
        string? errorMessage,
        StagedFareOptions? stagedFareOptions = null)
    {
        var response = BuildSearchResponse(totalCombinations, fareOptions);
        var stagedResults = stagedFareOptions is null
            ? null
            : new SearchStagedResults(
                BuildSearchResponse(totalCombinations, stagedFareOptions.OutboundFareOptions).Results,
                BuildSearchResponse(totalCombinations, stagedFareOptions.InboundFareOptions).Results,
                BuildSearchResponse(totalCombinations, stagedFareOptions.RoundTripFareOptions).Results);

        return new SearchSessionResponse(
            searchId,
            status,
            totalCombinations,
            completedCombinations,
            failedCombinations,
            response,
            errorMessage,
            stagedResults);
    }

    private static SearchResponse BuildSelectedReturnResponse(
        int searchCombinationCount,
        SearchStagedResults stagedResults,
        string outboundLegId)
    {
        var outbound = stagedResults.OutboundResults.FirstOrDefault(result =>
            string.Equals(result.Legs.FirstOrDefault()?.Id, outboundLegId, StringComparison.Ordinal));
        if (outbound is null)
        {
            return BuildSearchResponseFromResults(searchCombinationCount, [], 0, EmptyQuery);
        }

        var syntheticResults = stagedResults.InboundResults
            .Where(inbound =>
                HasBookableFare(outbound) &&
                HasBookableFare(inbound) &&
                IsCompatibleReturn(outbound, inbound))
            .Select(inbound => BuildSyntheticReturnResult(outbound, inbound))
            .ToList();
        var actualResults = stagedResults.RoundTripResults
            .Where(result => string.Equals(
                result.Legs.FirstOrDefault()?.Id,
                outboundLegId,
                StringComparison.Ordinal));
        var results = syntheticResults
            .Concat(actualResults)
            .OrderBy(result => result.PriceOptions[0].TotalPrice.Amount)
            .ThenBy(result => result.TotalDurationMinutes)
            .ToList();

        return BuildSearchResponseFromResults(
            searchCombinationCount,
            results,
            results.Sum(result => result.PriceOptions.Count),
            EmptyQuery);
    }

    private static bool IsCompatibleReturn(SearchResult outbound, SearchResult inbound)
    {
        var outboundArrival = outbound.Legs.LastOrDefault()?.ArrivalLocalTime;
        var inboundDeparture = inbound.Legs.FirstOrDefault()?.DepartureLocalTime;
        return outboundArrival is not null &&
            inboundDeparture is not null &&
            inboundDeparture >= outboundArrival;
    }

    private static bool HasBookableFare(SearchResult result) =>
        result.PriceOptions.FirstOrDefault()?.BookingLinks.Any(link =>
            !string.IsNullOrWhiteSpace(link.Url)) == true;

    private static SearchResult BuildSyntheticReturnResult(SearchResult outbound, SearchResult inbound)
    {
        var outboundPrice = outbound.PriceOptions[0];
        var inboundPrice = inbound.PriceOptions[0];
        var priceOption = new SearchResultPriceOption(
            $"synthetic:{outboundPrice.Id}:{inboundPrice.Id}",
            BuildSyntheticProviderName(outboundPrice.Provider, inboundPrice.Provider),
            new SearchResultPrice(
                outboundPrice.TotalPrice.Amount + inboundPrice.TotalPrice.Amount,
                outboundPrice.TotalPrice.Currency),
            [
                .. outboundPrice.BookingLinks
                    .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                    .Select(link =>
                    new SearchResultBookingLink("Book outbound", link.Url, outboundPrice.TotalPrice)),
                .. inboundPrice.BookingLinks
                    .Where(link => !string.IsNullOrWhiteSpace(link.Url))
                    .Select(link =>
                    new SearchResultBookingLink("Book return", link.Url, inboundPrice.TotalPrice))
            ]);

        return new SearchResult(
            $"synthetic:{outbound.Id}:{inbound.Id}",
            true,
            [.. outbound.Legs, .. inbound.Legs],
            outbound.TotalDurationMinutes + inbound.TotalDurationMinutes,
            [priceOption]);
    }

    private static SearchResponse BuildSearchResponse(
        int searchCombinationCount,
        List<SearchFareOption> fareOptions)
    {
        var groupedResults = fareOptions
            .GroupBy(BuildFlightGroupingKey)
            .Select(group =>
            {
                var cheapestOption = group
                    .OrderBy(option => option.TotalPrice.Amount)
                    .ThenBy(option => option.Provider)
                    .First();

                var priceOptions = group
                    .Select(option => new SearchResultPriceOption(
                        option.Id,
                        option.Provider,
                        option.TotalPrice,
                        option.BookingLinks))
                    .OrderBy(option => option.TotalPrice.Amount)
                    .ThenBy(option => option.Provider)
                    .ToList();

                return new SearchResult(
                    cheapestOption.FlightId,
                    cheapestOption.IsRoundTrip,
                    cheapestOption.Legs,
                    cheapestOption.TotalDurationMinutes,
                    priceOptions);
            })
            .OrderBy(result => result.PriceOptions[0].TotalPrice.Amount)
            .ThenBy(result => result.TotalDurationMinutes)
            .ToList();

        return BuildSearchResponseFromResults(searchCombinationCount, groupedResults, fareOptions.Count, EmptyQuery);
    }

    private static SearchResponse BuildFilteredSearchResponse(SearchResponse response, SearchResultsQuery query) =>
        BuildSearchResponseFromResults(
            response.Metadata.SearchCombinationCount,
            response.Results,
            response.Metadata.ProviderResultCount,
            query);

    private static SearchResponse BuildSearchResponseFromResults(
        int searchCombinationCount,
        List<SearchResult> canonicalResults,
        int providerResultCount,
        SearchResultsQuery query)
    {
        var filterLegIndex = query.GetOutboundLegId() is null ? 0 : 1;
        var globalFilters = BuildGlobalFilterMetadata(canonicalResults, filterLegIndex);
        var baseFilteredResults = ApplyBaseFilters(canonicalResults, query, filterLegIndex).ToList();
        var filteredResults = ApplyFinalFilters(baseFilteredResults, query, filterLegIndex).ToList();
        var stopFacetResults = ApplyFinalFilters(
            ApplyBaseFilters(canonicalResults, query, filterLegIndex, applyStopFilter: false),
            query,
            filterLegIndex);
        var pagination = ApplyPagination(filteredResults, query, out var pagedResults);
        var returnedStopCounts = BuildStopCounts(pagedResults, filterLegIndex);
        var visibleProviderResultCount = filteredResults.Sum(result => result.PriceOptions.Count);

        return new SearchResponse(
            pagedResults,
            new SearchMetadata(
                searchCombinationCount,
                visibleProviderResultCount,
                pagedResults.Count,
                returnedStopCounts.DirectFlightCount,
                returnedStopCounts.OneStopFlightCount,
                returnedStopCounts.TwoPlusStopFlightCount),
            BuildFiltersMetadata(globalFilters, baseFilteredResults, stopFacetResults, filterLegIndex),
            pagination);
    }

    private static void ValidateRequest(SearchRequest request)
    {
        if (request.OriginAirports.Count == 0)
        {
            throw new ArgumentException("At least one origin airport is required.");
        }

        if (request.DestinationAirports.Count == 0)
        {
            throw new ArgumentException("At least one destination airport is required.");
        }

        var departureDates = request.GetDepartureDates().ToList();
        var returnDates = request.GetReturnDates().ToList();
        if (returnDates.Count > 0)
        {
            var hasValidRoundTripPair = departureDates.Any(departureDate =>
                returnDates.Any(returnDate => returnDate >= departureDate));
            if (!hasValidRoundTripPair)
            {
                throw new ArgumentException(
                    "At least one return date must be on or after a selected departure date.");
            }
        }

        if (request.Adults <= 0)
        {
            throw new ArgumentException("At least one adult passenger is required.");
        }
    }

    private static void ValidateProviderCallCount(long providerCallCount, SearchLimit searchLimit)
    {
        if (providerCallCount <= 0)
        {
            throw new ArgumentException("Search must contain at least one valid origin, destination, and departure date combination.");
        }

        if (searchLimit.MaxSearchCombinations is not null && providerCallCount > searchLimit.MaxSearchCombinations.Value)
        {
            throw new ArgumentException(searchLimit.ExceededMessage ?? $"Search exceeds the limit of {searchLimit.MaxSearchCombinations.Value} combinations.");
        }

        if (providerCallCount > MaxMaterializedProviderCalls)
        {
            throw new ArgumentException($"Search exceeds the safety limit of {MaxMaterializedProviderCalls} combinations.");
        }
    }

    private static long CountProviderCalls(SearchRequest request)
    {
        var origins = request.OriginAirports.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var destinations = request.DestinationAirports.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var departureDates = request.GetDepartureDates().ToList();
        var routeCount = origins.Sum(origin => (long)destinations.Count(destination =>
            !string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase)));

        var outboundCount = routeCount * departureDates.Count;
        var returnDates = request.GetReturnDates().ToList();
        if (returnDates.Count == 0)
        {
            return outboundCount;
        }

        var returnDateCount = returnDates.Count;
        var validRoundTripDatePairCount = departureDates.Sum(departureDate =>
            (long)returnDates.Count(returnDate => returnDate >= departureDate));

        return outboundCount +
            (routeCount * returnDateCount) +
            (routeCount * validRoundTripDatePairCount);
    }

    private static SearchPlan BuildSearchPlan(SearchRequest request, string currency)
    {
        var outboundRequests = ExpandOneWaySearches(request, currency).ToList();
        if (!request.GetReturnDates().Any())
        {
            return new SearchPlan(
                IsRoundTripSearch: false,
                OutboundRequests: outboundRequests,
                InboundRequests: [],
                RoundTripRequests: []);
        }

        var inboundRequests = ExpandInboundSearches(request, currency).ToList();
        var roundTripRequests = ExpandRoundTripSearches(request, currency).ToList();

        return new SearchPlan(
            IsRoundTripSearch: true,
            OutboundRequests: outboundRequests,
            InboundRequests: inboundRequests,
            RoundTripRequests: roundTripRequests);
    }

    private static IEnumerable<ProviderSearchRequest> ExpandOneWaySearches(SearchRequest request, string currency)
    {
        foreach (var origin in request.OriginAirports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var destination in request.DestinationAirports.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var departureDate in request.GetDepartureDates())
                {
                    yield return new ProviderSearchRequest(
                        origin.ToUpperInvariant(),
                        destination.ToUpperInvariant(),
                        departureDate,
                        request.Adults,
                        request.CabinClass,
                        currency);
                }
            }
        }
    }

    private static IEnumerable<ProviderSearchRequest> ExpandInboundSearches(SearchRequest request, string currency)
    {
        foreach (var destination in request.DestinationAirports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var origin in request.OriginAirports.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var returnDate in request.GetReturnDates())
                {
                    yield return new ProviderSearchRequest(
                        destination.ToUpperInvariant(),
                        origin.ToUpperInvariant(),
                        returnDate,
                        request.Adults,
                        request.CabinClass,
                        currency);
                }
            }
        }
    }

    private static IEnumerable<ProviderRoundTripSearchRequest> ExpandRoundTripSearches(SearchRequest request, string currency)
    {
        foreach (var origin in request.OriginAirports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var destination in request.DestinationAirports.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var departureDate in request.GetDepartureDates())
                {
                    foreach (var returnDate in request.GetReturnDates())
                    {
                        if (returnDate < departureDate)
                        {
                            continue;
                        }

                        yield return new ProviderRoundTripSearchRequest(
                            origin.ToUpperInvariant(),
                            destination.ToUpperInvariant(),
                            departureDate,
                            returnDate,
                            request.Adults,
                            request.CabinClass,
                            currency);
                    }
                }
            }
        }
    }

    private IEnumerable<SearchFareOption> MapToSearchFareOptions(FlightApiOneWayResponse response, bool isRoundTrip)
    {
        var legsById = BuildProviderLookup(response.Legs, leg => leg.Id, null, out var duplicateLegs);
        var segmentsById = BuildProviderLookup(response.Segments, segment => segment.Id, null, out var duplicateSegments);
        var placesById = BuildProviderLookup(response.Places, place => place.Id, null, out var duplicatePlaces);
        var carriersById = BuildProviderLookup(response.Carriers, carrier => carrier.Id, null, out var duplicateCarriers);
        var agentsById = BuildProviderLookup(
            response.Agents.Where(agent => !string.IsNullOrWhiteSpace(agent.Id)),
            agent => agent.Id,
            StringComparer.OrdinalIgnoreCase,
            out var duplicateAgents);
        var duplicateEntityCount = duplicateLegs + duplicateSegments + duplicatePlaces + duplicateCarriers + duplicateAgents;
        if (duplicateEntityCount > 0)
        {
            logger.LogInformation(
                "Normalized {DuplicateEntityCount} duplicate entities in a FlightApi response",
                duplicateEntityCount);
        }

        foreach (var itinerary in response.Itineraries)
        {
            var resultLegs = itinerary.LegIds
                .Select(legId => BuildSearchResultLeg(legId, legsById, segmentsById, placesById, carriersById))
                .Where(leg => leg is not null)
                .Cast<SearchResultLeg>()
                .ToList();

            if (resultLegs.Count == 0)
            {
                continue;
            }

            var pricingItems = itinerary.PricingOptions
                .SelectMany(pricingOption => MapPricingItems(itinerary, pricingOption, resultLegs, agentsById, isRoundTrip));

            foreach (var result in pricingItems)
            {
                yield return result;
            }
        }
    }

    private static Dictionary<TKey, TValue> BuildProviderLookup<TValue, TKey>(
        IEnumerable<TValue> values,
        Func<TValue, TKey> keySelector,
        IEqualityComparer<TKey>? comparer,
        out int duplicateCount)
        where TKey : notnull
    {
        var lookup = new Dictionary<TKey, TValue>(comparer);
        duplicateCount = 0;

        foreach (var value in values)
        {
            if (!lookup.TryAdd(keySelector(value), value))
            {
                duplicateCount += 1;
            }
        }

        return lookup;
    }

    private static IEnumerable<SearchFareOption> MapPricingItems(
        FlightApiItinerary itinerary,
        FlightApiPricingOption pricingOption,
        List<SearchResultLeg> resultLegs,
        IReadOnlyDictionary<string, FlightApiAgent> agentsById,
        bool isRoundTrip)
    {
        if (pricingOption.Items.Count == 0)
        {
            var amount = pricingOption.Price?.Amount;
            if (amount is null)
            {
                yield break;
            }

            yield return BuildSearchFareOption(
                $"{itinerary.Id}:{pricingOption.Id}",
                itinerary.Id,
                amount.Value,
                [BuildBookingLink("View fare", BuildDeepLink(itinerary.DeepLink))],
                resultLegs,
                isRoundTrip);
            yield break;
        }

        foreach (var item in pricingOption.Items)
        {
            var amount = ResolvePriceAmount(item.Price?.Amount, pricingOption.Price?.Amount, item.Url, itinerary.DeepLink);
            if (amount is null)
            {
                continue;
            }

            var agentName = ResolveAgentName(item.AgentId, pricingOption.AgentIds, agentsById);

            yield return BuildSearchFareOption(
                $"{itinerary.Id}:{pricingOption.Id}:{item.AgentId ?? "unknown"}",
                itinerary.Id,
                amount.Value,
                [BuildBookingLink("View fare", BuildDeepLink(item.Url ?? itinerary.DeepLink))],
                resultLegs,
                isRoundTrip,
                agentName);
        }
    }

    private static SearchFareOption BuildSearchFareOption(
        string id,
        string flightId,
        decimal amount,
        List<SearchResultBookingLink> bookingLinks,
        List<SearchResultLeg> legs,
        bool isRoundTrip,
        string? agentName = null) =>
        new(
            id,
            flightId,
            string.IsNullOrWhiteSpace(agentName) ? ProviderName : $"{ProviderName}:{agentName}",
            isRoundTrip,
            new SearchResultPrice(amount, "EUR"),
            legs,
            legs.Sum(leg => leg.DurationMinutes),
            bookingLinks);

    private static SearchResultBookingLink BuildBookingLink(string label, string url, SearchResultPrice? price = null) =>
        new(label, url, price);

    private static string BuildSyntheticProviderName(string outboundProvider, string inboundProvider)
    {
        var outboundLabel = outboundProvider.Replace($"{ProviderName}:", string.Empty, StringComparison.OrdinalIgnoreCase);
        var inboundLabel = inboundProvider.Replace($"{ProviderName}:", string.Empty, StringComparison.OrdinalIgnoreCase);
        return $"{ProviderName}:Combined one-way ({outboundLabel} + {inboundLabel})";
    }

    private static string BuildFlightGroupingKey(SearchFareOption option)
    {
        var legKey = string.Join(
            "|",
            option.Legs.Select(leg =>
                $"{leg.OriginAirport}-{leg.DestinationAirport}-{leg.DepartureLocalTime:O}-{leg.ArrivalLocalTime:O}-{leg.DurationMinutes}"));

        var segmentKey = string.Join(
            "|",
            option.Legs.SelectMany(leg => leg.Segments)
                .Select(segment =>
                    $"{segment.MarketingCarrierCode}-{segment.FlightNumber}-{segment.OriginAirport}-{segment.DestinationAirport}-{segment.DepartureLocalTime:O}-{segment.ArrivalLocalTime:O}"));

        return $"{option.IsRoundTrip}:{legKey}:{segmentKey}";
    }

    private static decimal? ResolvePriceAmount(
        decimal? itemAmount,
        decimal? pricingOptionAmount,
        string? itemUrl,
        string? itineraryDeepLink)
    {
        if (itemAmount is > 0)
        {
            return itemAmount.Value;
        }

        if (pricingOptionAmount is > 0)
        {
            return pricingOptionAmount.Value;
        }

        return ParseTicketPrice(itemUrl) ?? ParseTicketPrice(itineraryDeepLink);
    }

    private static SearchResultLeg? BuildSearchResultLeg(
        string legId,
        IReadOnlyDictionary<string, FlightApiLeg> legsById,
        IReadOnlyDictionary<string, FlightApiSegment> segmentsById,
        IReadOnlyDictionary<int, FlightApiPlace> placesById,
        IReadOnlyDictionary<int, FlightApiCarrier> carriersById)
    {
        if (!legsById.TryGetValue(legId, out var leg))
        {
            return null;
        }

        var segments = leg.SegmentIds
            .Select(segmentId => BuildSearchResultSegment(segmentId, segmentsById, placesById, carriersById))
            .Where(segment => segment is not null)
            .Cast<SearchResultSegment>()
            .ToList();

        var searchLeg = new SearchResultLeg(
            string.Empty,
            ResolvePlaceCode(leg.OriginPlaceId, placesById),
            ResolvePlaceCode(leg.DestinationPlaceId, placesById),
            DateTime.SpecifyKind(leg.Departure, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(leg.Arrival, DateTimeKind.Unspecified),
            leg.Duration,
            segments);

        return searchLeg with { Id = BuildLegFilterKey(searchLeg) };
    }

    private static SearchResultSegment? BuildSearchResultSegment(
        string segmentId,
        IReadOnlyDictionary<string, FlightApiSegment> segmentsById,
        IReadOnlyDictionary<int, FlightApiPlace> placesById,
        IReadOnlyDictionary<int, FlightApiCarrier> carriersById)
    {
        if (!segmentsById.TryGetValue(segmentId, out var segment))
        {
            return null;
        }

        return new SearchResultSegment(
            ResolveCarrierName(segment.MarketingCarrierId, carriersById),
            ResolveCarrierCode(segment.MarketingCarrierId, carriersById),
            segment.MarketingFlightNumber ?? string.Empty,
            ResolvePlaceCode(segment.OriginPlaceId, placesById),
            ResolvePlaceCode(segment.DestinationPlaceId, placesById),
            DateTime.SpecifyKind(segment.Departure, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(segment.Arrival, DateTimeKind.Unspecified),
            segment.Duration);
    }

    private static string ResolvePlaceCode(
        int placeId,
        IReadOnlyDictionary<int, FlightApiPlace> placesById)
    {
        if (!placesById.TryGetValue(placeId, out var place))
        {
            return placeId.ToString();
        }

        return FirstNonEmpty(
            place.Iata,
            place.DisplayCode,
            GetAdditionalString(place.AdditionalData, "iata_code"),
            GetAdditionalString(place.AdditionalData, "sky_code"),
            place.Name,
            placeId.ToString());
    }

    private static string ResolveCarrierCode(
        int? carrierId,
        IReadOnlyDictionary<int, FlightApiCarrier> carriersById)
    {
        if (carrierId is null || !carriersById.TryGetValue(carrierId.Value, out var carrier))
        {
            return carrierId?.ToString() ?? string.Empty;
        }

        return FirstNonEmpty(
            carrier.DisplayCode,
            carrier.Name,
            GetAdditionalString(carrier.AdditionalData, "iata"),
            GetAdditionalString(carrier.AdditionalData, "iata_code"),
            carrierId.Value.ToString());
    }

    private static string ResolveCarrierName(
        int? carrierId,
        IReadOnlyDictionary<int, FlightApiCarrier> carriersById)
    {
        if (carrierId is null || !carriersById.TryGetValue(carrierId.Value, out var carrier))
        {
            return carrierId?.ToString() ?? string.Empty;
        }

        return FirstNonEmpty(
            carrier.Name,
            carrier.DisplayCode,
            GetAdditionalString(carrier.AdditionalData, "name"),
            carrierId.Value.ToString());
    }

    private static string? ResolveAgentName(
        string? itemAgentId,
        List<string> agentIds,
        IReadOnlyDictionary<string, FlightApiAgent> agentsById)
    {
        var agentId = !string.IsNullOrWhiteSpace(itemAgentId)
            ? itemAgentId
            : agentIds.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(agentId) || !agentsById.TryGetValue(agentId, out var agent))
        {
            return null;
        }

        return FirstNonEmpty(
            agent.Name,
            GetAdditionalString(agent.AdditionalData, "display_name"),
            agentId);
    }

    private static string BuildDeepLink(string? rawLink)
    {
        if (string.IsNullOrWhiteSpace(rawLink))
        {
            return string.Empty;
        }

        var trimmedLink = rawLink.Trim();

        if (Uri.TryCreate(trimmedLink, UriKind.Absolute, out var absoluteUri))
        {
            if (string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.ToString();
            }
        }

        return $"https://www.skyscanner.nl/{trimmedLink.TrimStart('/')}";
    }

    private static decimal? ParseTicketPrice(string? rawLink)
    {
        if (string.IsNullOrWhiteSpace(rawLink))
        {
            return null;
        }

        var absoluteLink = BuildDeepLink(rawLink);
        if (!Uri.TryCreate(absoluteLink, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !string.Equals(parts[0], "ticket_price", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decoded = Uri.UnescapeDataString(parts[1]);
            if (decimal.TryParse(
                decoded,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedAmount))
            {
                return parsedAmount;
            }
        }

        return null;
    }

    private static string? GetAdditionalString(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? additionalData,
        string key)
    {
        if (additionalData is null || !additionalData.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static StopCounts BuildStopCounts(IEnumerable<SearchResult> results, int legIndex = 0)
    {
        var direct = 0;
        var oneStop = 0;
        var twoPlusStops = 0;

        foreach (var result in results)
        {
            var stops = result.Legs.Count <= legIndex
                ? 0
                : Math.Max(result.Legs[legIndex].Segments.Count - 1, 0);

            if (stops == 0)
            {
                direct += 1;
                continue;
            }

            if (stops == 1)
            {
                oneStop += 1;
                continue;
            }

            twoPlusStops += 1;
        }

        return new StopCounts(direct, oneStop, twoPlusStops);
    }

    private static List<SearchResult> ApplyBaseFilters(
        IEnumerable<SearchResult> results,
        SearchResultsQuery query,
        int filterLegIndex,
        bool applyStopFilter = true)
    {
        var providers = query.GetProviders();
        var airlines = query.GetAirlines();
        var departureAirports = query.GetDepartureAirports();
        var arrivalAirports = query.GetArrivalAirports();
        var departureTimeRange = query.GetDepartureTimeRange();
        var arrivalTimeRange = query.GetArrivalTimeRange();
        var returnDepartureTimeRange = query.GetReturnDepartureTimeRange();
        var returnArrivalTimeRange = query.GetReturnArrivalTimeRange();
        var outboundLegId = query.GetOutboundLegId();
        var returnLegId = query.GetReturnLegId();

        return results
            .Select(result =>
            {
                var visiblePriceOptions = providers.Count == 0
                    ? [.. result.PriceOptions]
                    : result.PriceOptions
                        .Where(option => providers.Contains(option.Provider, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(option => option.TotalPrice.Amount)
                        .ThenBy(option => option.Provider, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                return visiblePriceOptions.Count == 0
                    ? null
                    : result with { PriceOptions = visiblePriceOptions };
            })
            .Where(result => result is not null)
            .Cast<SearchResult>()
            .Where(result => !applyStopFilter || MatchesStopFilters(result, query, filterLegIndex))
            .Where(result => MatchesAirlineFilters(result, airlines, filterLegIndex))
            .Where(result => MatchesAirportFilters(result, departureAirports, arrivalAirports, filterLegIndex))
            .Where(result => MatchesTimeFilters(result, departureTimeRange, arrivalTimeRange, returnDepartureTimeRange, returnArrivalTimeRange))
            .Where(result => MatchesLegFilters(result, outboundLegId, returnLegId))
            .ToList();
    }

    private static List<SearchResult> ApplyFinalFilters(
        IEnumerable<SearchResult> results,
        SearchResultsQuery query,
        int filterLegIndex)
        => results
            .Where(result => !query.MaxDuration.HasValue ||
                (result.Legs.ElementAtOrDefault(filterLegIndex)?.DurationMinutes ?? result.TotalDurationMinutes) <= query.MaxDuration.Value)
            .ToList();

    private static SearchPagination ApplyPagination(
        List<SearchResult> results,
        SearchResultsQuery query,
        out List<SearchResult> pagedResults)
    {
        if (!query.IsPaginationRequested)
        {
            pagedResults = [.. results];
            var pageSize = results.Count == 0 ? 0 : results.Count;
            return new SearchPagination(1, pageSize, results.Count, results.Count == 0 ? 0 : 1);
        }

        var page = query.GetPage();
        var pageSizeWithDefault = query.GetPageSize();
        var totalResults = results.Count;
        var totalPages = totalResults == 0 ? 0 : (int)Math.Ceiling(totalResults / (double)pageSizeWithDefault);
        var skip = (page - 1) * pageSizeWithDefault;

        pagedResults = results
            .Skip(skip)
            .Take(pageSizeWithDefault)
            .ToList();

        return new SearchPagination(page, pageSizeWithDefault, totalResults, totalPages);
    }

    private static SearchFiltersMetadata BuildGlobalFilterMetadata(List<SearchResult> results, int filterLegIndex) =>
        new(
            BuildProviderCounts(results),
            BuildAirlineCounts(results, filterLegIndex),
            BuildAirportCounts(results, result => result.Legs.ElementAtOrDefault(filterLegIndex)?.OriginAirport),
            BuildAirportCounts(results, result => result.Legs.ElementAtOrDefault(filterLegIndex)?.DestinationAirport),
            BuildDurationRange(results, filterLegIndex),
            BuildTimeRange(results, 0, leg => leg.DepartureLocalTime),
            BuildTimeRange(results, 0, leg => leg.ArrivalLocalTime),
            BuildTimeRange(results, 1, leg => leg.DepartureLocalTime),
            BuildTimeRange(results, 1, leg => leg.ArrivalLocalTime),
            BuildStopFilterMetadata(results, filterLegIndex));

    private static SearchFiltersMetadata BuildFiltersMetadata(
        SearchFiltersMetadata globalFilters,
        List<SearchResult> baseFilteredResults,
        List<SearchResult> stopFacetResults,
        int filterLegIndex) =>
        globalFilters with
        {
            DurationMinutes = BuildDurationRange(baseFilteredResults, filterLegIndex),
            DepartureTimeMinutes = BuildTimeRange(baseFilteredResults, 0, leg => leg.DepartureLocalTime),
            ArrivalTimeMinutes = BuildTimeRange(baseFilteredResults, 0, leg => leg.ArrivalLocalTime),
            ReturnDepartureTimeMinutes = BuildTimeRange(baseFilteredResults, 1, leg => leg.DepartureLocalTime),
            ReturnArrivalTimeMinutes = BuildTimeRange(baseFilteredResults, 1, leg => leg.ArrivalLocalTime),
            Stops = BuildStopFilterMetadata(stopFacetResults, filterLegIndex)
        };

    private static List<SearchFilterOptionCount> BuildProviderCounts(IEnumerable<SearchResult> results) =>
        results
            .SelectMany(result => result.PriceOptions)
            .GroupBy(option => option.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SearchFilterOptionCount(group.Key, group.Count()))
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<SearchFilterOptionCount> BuildAirlineCounts(IEnumerable<SearchResult> results, int legIndex) =>
        results
            .Select(result => result.Legs.ElementAtOrDefault(legIndex))
            .Where(leg => leg is not null)
            .Cast<SearchResultLeg>()
            .SelectMany(leg => leg.Segments)
            .Where(segment => !string.IsNullOrWhiteSpace(segment.MarketingCarrierName))
            .GroupBy(segment => segment.MarketingCarrierName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SearchFilterOptionCount(group.Key, group.Count()))
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<SearchFilterOptionCount> BuildAirportCounts(
        IEnumerable<SearchResult> results,
        Func<SearchResult, string?> selector) =>
        results
            .Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SearchFilterOptionCount(group.Key, group.Count()))
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static SearchRangeMetadata BuildDurationRange(IEnumerable<SearchResult> results, int legIndex)
    {
        var durations = results
            .Select(result => result.Legs.ElementAtOrDefault(legIndex)?.DurationMinutes ?? result.TotalDurationMinutes)
            .ToList();
        if (durations.Count == 0)
        {
            return new SearchRangeMetadata(0, 0);
        }

        return new SearchRangeMetadata(durations.Min(), durations.Max());
    }

    private static SearchRangeMetadata BuildTimeRange(
        IEnumerable<SearchResult> results,
        int legIndex,
        Func<SearchResultLeg, DateTime> selector)
    {
        var minutes = results
            .Select(result => result.Legs.ElementAtOrDefault(legIndex))
            .Where(leg => leg is not null)
            .Cast<SearchResultLeg>()
            .Select(leg => GetClockMinutes(selector(leg)))
            .ToList();

        if (minutes.Count == 0)
        {
            return new SearchRangeMetadata(0, 0);
        }

        return new SearchRangeMetadata(minutes.Min(), minutes.Max());
    }

    private static SearchStopFilterMetadata BuildStopFilterMetadata(IEnumerable<SearchResult> results, int legIndex)
    {
        var resultList = results.ToList();
        var stopCounts = BuildStopCounts(resultList, legIndex);
        var minimumAvailableStops = resultList.Count == 0
            ? (int?)null
            : resultList.Min(result => result.Legs.Count <= legIndex
                ? 0
                : Math.Max(result.Legs[legIndex].Segments.Count - 1, 0));
        return new SearchStopFilterMetadata(
            stopCounts.DirectFlightCount,
            stopCounts.OneStopFlightCount,
            stopCounts.TwoPlusStopFlightCount,
            minimumAvailableStops);
    }

    private static bool MatchesStopFilters(SearchResult result, SearchResultsQuery query, int legIndex)
    {
        if (!query.HasStopFilter)
        {
            return true;
        }

        var stopCount = result.Legs.Count <= legIndex
            ? 0
            : Math.Max(result.Legs[legIndex].Segments.Count - 1, 0);

        if (query.ExactStops.HasValue)
        {
            return stopCount == Math.Max(query.ExactStops.Value, 0);
        }

        return stopCount switch
        {
            0 => query.Direct ?? false,
            1 => query.OneStop ?? false,
            _ => query.TwoPlusStop ?? false
        };
    }

    private static bool MatchesAirlineFilters(SearchResult result, List<string> airlines, int legIndex)
    {
        if (airlines.Count == 0)
        {
            return true;
        }

        return result.Legs
            .ElementAtOrDefault(legIndex)?
            .Segments
            .Any(segment => airlines.Contains(segment.MarketingCarrierName, StringComparer.OrdinalIgnoreCase)) == true;
    }

    private static bool MatchesAirportFilters(
        SearchResult result,
        List<string> departureAirports,
        List<string> arrivalAirports,
        int legIndex)
    {
        var departureAirport = result.Legs.ElementAtOrDefault(legIndex)?.OriginAirport;
        var arrivalAirport = result.Legs.ElementAtOrDefault(legIndex)?.DestinationAirport;

        var matchesDepartureAirport = departureAirports.Count == 0 ||
            (!string.IsNullOrWhiteSpace(departureAirport) &&
             departureAirports.Contains(departureAirport, StringComparer.OrdinalIgnoreCase));

        var matchesArrivalAirport = arrivalAirports.Count == 0 ||
            (!string.IsNullOrWhiteSpace(arrivalAirport) &&
             arrivalAirports.Contains(arrivalAirport, StringComparer.OrdinalIgnoreCase));

        return matchesDepartureAirport && matchesArrivalAirport;
    }

    private static bool MatchesTimeFilters(
        SearchResult result,
        (int Min, int Max)? departureTimeRange,
        (int Min, int Max)? arrivalTimeRange,
        (int Min, int Max)? returnDepartureTimeRange,
        (int Min, int Max)? returnArrivalTimeRange)
    {
        var firstLeg = result.Legs.FirstOrDefault();
        if (firstLeg is null)
        {
            return false;
        }

        var departureMinutes = GetClockMinutes(firstLeg.DepartureLocalTime);
        var arrivalMinutes = GetClockMinutes(firstLeg.ArrivalLocalTime);

        var matchesDeparture = !departureTimeRange.HasValue ||
            (departureMinutes >= departureTimeRange.Value.Min && departureMinutes <= departureTimeRange.Value.Max);
        var matchesArrival = !arrivalTimeRange.HasValue ||
            (arrivalMinutes >= arrivalTimeRange.Value.Min && arrivalMinutes <= arrivalTimeRange.Value.Max);

        if (!matchesDeparture || !matchesArrival)
        {
            return false;
        }

        if (!returnDepartureTimeRange.HasValue && !returnArrivalTimeRange.HasValue)
        {
            return true;
        }

        var returnLeg = result.Legs.ElementAtOrDefault(1);
        if (returnLeg is null)
        {
            return false;
        }

        var returnDepartureMinutes = GetClockMinutes(returnLeg.DepartureLocalTime);
        var returnArrivalMinutes = GetClockMinutes(returnLeg.ArrivalLocalTime);

        var matchesReturnDeparture = !returnDepartureTimeRange.HasValue ||
            (returnDepartureMinutes >= returnDepartureTimeRange.Value.Min && returnDepartureMinutes <= returnDepartureTimeRange.Value.Max);
        var matchesReturnArrival = !returnArrivalTimeRange.HasValue ||
            (returnArrivalMinutes >= returnArrivalTimeRange.Value.Min && returnArrivalMinutes <= returnArrivalTimeRange.Value.Max);

        return matchesReturnDeparture && matchesReturnArrival;
    }

    private static bool MatchesLegFilters(
        SearchResult result,
        string? outboundLegId,
        string? returnLegId)
    {
        if (outboundLegId is null && returnLegId is null)
        {
            return true;
        }

        var outboundLeg = result.Legs.ElementAtOrDefault(0);
        var returnLeg = result.Legs.ElementAtOrDefault(1);

        var matchesOutbound = outboundLegId is null ||
            (outboundLeg is not null && string.Equals(outboundLeg.Id, outboundLegId, StringComparison.Ordinal));
        var matchesReturn = returnLegId is null ||
            (returnLeg is not null && string.Equals(returnLeg.Id, returnLegId, StringComparison.Ordinal));

        return matchesOutbound && matchesReturn;
    }

    private static int GetClockMinutes(DateTime dateTime) => (dateTime.Hour * 60) + dateTime.Minute;

    private static string BuildLegFilterKey(SearchResultLeg leg)
    {
        var segmentKey = string.Join(
            "|",
            leg.Segments.Select(segment =>
                $"{segment.MarketingCarrierCode}-{segment.FlightNumber}-{segment.OriginAirport}-{segment.DestinationAirport}-{segment.DepartureLocalTime:O}-{segment.ArrivalLocalTime:O}"));

        return $"{leg.OriginAirport}-{leg.DestinationAirport}-{leg.DepartureLocalTime:O}-{leg.ArrivalLocalTime:O}-{segmentKey}";
    }

    private sealed record SearchFareOption(
        string Id,
        string FlightId,
        string Provider,
        bool IsRoundTrip,
        SearchResultPrice TotalPrice,
        List<SearchResultLeg> Legs,
        int TotalDurationMinutes,
        List<SearchResultBookingLink> BookingLinks);

    private sealed record StopCounts(
        int DirectFlightCount,
        int OneStopFlightCount,
        int TwoPlusStopFlightCount);
}
