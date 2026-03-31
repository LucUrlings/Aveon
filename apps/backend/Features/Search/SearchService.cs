using backend.Features.Search.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace backend.Features.Search;

public sealed class SearchService(
    IServiceScopeFactory serviceScopeFactory,
    ISearchSessionStore searchSessionStore,
    ILogger<SearchService> logger) : ISearchService
{
    private const int MaxSearchCombinations = 60;
    private const int MaxConcurrentProviderCalls = 5;
    private const int MaxResults = 2000;
    private const string ProviderName = "FlightApi";
    private const string StatusRunning = "running";
    private const string StatusCompleted = "completed";
    private const string StatusFailed = "failed";
    private static readonly SearchResultsQuery EmptyQuery = new();

    public async Task<SearchSessionResponse> StartSearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var combinations = ExpandOneWaySearches(request).ToList();
        Validate(request, combinations.Count);
        var searchId = Guid.NewGuid().ToString("N");

        var initialSession = new SearchSessionResponse(
            searchId,
            StatusRunning,
            combinations.Count,
            0,
            0,
            BuildSearchResponse(combinations.Count, new List<SearchFareOption>()),
            null);

        await searchSessionStore.SetAsync(initialSession, cancellationToken);

        _ = Task.Run(() => RunSearchSafelyAsync(searchId, combinations), CancellationToken.None);

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
        return session with
        {
            Response = BuildFilteredSearchResponse(session.Response, effectiveQuery)
        };
    }

    private async Task RunSearchSafelyAsync(string searchId, IReadOnlyList<ProviderSearchRequest> combinations)
    {
        var executionState = new SearchExecutionState();

        try
        {
            await RunSearchAsync(searchId, combinations, executionState);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background flight search failed for search session {SearchId}", searchId);

            var snapshot = executionState.Capture();
            var failedSession = BuildSessionSnapshot(
                searchId,
                StatusFailed,
                combinations.Count,
                snapshot.CompletedCombinations,
                Math.Max(snapshot.FailedCombinations, combinations.Count - snapshot.CompletedCombinations),
                snapshot.FareOptions,
                "Search failed before completion.");

            await TrySetSearchSessionAsync(
                failedSession,
                "persisting failed search session after an unexpected background error");
        }
    }

    private async Task RunSearchAsync(
        string searchId,
        IReadOnlyList<ProviderSearchRequest> combinations,
        SearchExecutionState executionState)
    {
        using var concurrencyGate = new SemaphoreSlim(MaxConcurrentProviderCalls);

        var providerTasks = combinations
            .Select(async candidate =>
            {
                await concurrencyGate.WaitAsync();
                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var flightSearchProvider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();

                    try
                    {
                        var providerResponse = await flightSearchProvider.SearchOneWayAsync(candidate, CancellationToken.None);
                        var mappedFareOptions = MapToSearchFareOptions(providerResponse).ToList();

                        var snapshot = executionState.BuildRunningSnapshot(searchId, combinations.Count, mappedFareOptions);

                        await TrySetSearchSessionAsync(
                            snapshot,
                            "persisting an in-progress search session update");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "FlightApi search failed for {OriginAirport} -> {DestinationAirport} on {DepartureDate}",
                            candidate.OriginAirport,
                            candidate.DestinationAirport,
                            candidate.DepartureDate);

                        var snapshot = executionState.BuildFailedProviderSnapshot(searchId, combinations.Count);

                        await TrySetSearchSessionAsync(
                            snapshot,
                            "persisting a failed provider search session update");
                    }
                }
                finally
                {
                    concurrencyGate.Release();
                }
            })
            .ToArray();

        await Task.WhenAll(providerTasks);

        var finalSession = executionState.BuildFinalSnapshot(searchId, combinations.Count);

        await TrySetSearchSessionAsync(finalSession, "persisting the final search session");
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

    private sealed class SearchExecutionState
    {
        private readonly Lock _lock = new();
        private readonly List<SearchFareOption> _fareOptions = [];
        private int _completedCombinations;
        private int _failedCombinations;

        public SearchSessionResponse BuildRunningSnapshot(
            string searchId,
            int totalCombinations,
            IReadOnlyCollection<SearchFareOption> newFareOptions)
        {
            lock (_lock)
            {
                _fareOptions.AddRange(newFareOptions);
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
                    [.. _fareOptions],
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
                [.. _fareOptions],
                errorMessage);
    }

    private sealed record SearchExecutionSnapshot(
        List<SearchFareOption> FareOptions,
        int CompletedCombinations,
        int FailedCombinations);

    private static SearchSessionResponse BuildSessionSnapshot(
        string searchId,
        string status,
        int totalCombinations,
        int completedCombinations,
        int failedCombinations,
        List<SearchFareOption> fareOptions,
        string? errorMessage) =>
        new(
            searchId,
            status,
            totalCombinations,
            completedCombinations,
            failedCombinations,
            BuildSearchResponse(totalCombinations, fareOptions),
            errorMessage);

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
                        option.DeepLink))
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
            .Take(MaxResults)
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
        var globalFilters = BuildGlobalFilterMetadata(canonicalResults);
        var baseFilteredResults = ApplyBaseFilters(canonicalResults, query).ToList();
        var filteredResults = ApplyFinalFilters(baseFilteredResults, query).ToList();
        var pagination = ApplyPagination(filteredResults, query, out var pagedResults);
        var returnedStopCounts = BuildStopCounts(pagedResults);
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
            BuildFiltersMetadata(globalFilters, baseFilteredResults),
            pagination);
    }

    private static void Validate(SearchRequest request, int expandedCombinationCount)
    {
        if (request.OriginAirports.Count == 0)
        {
            throw new ArgumentException("At least one origin airport is required.");
        }

        if (request.DestinationAirports.Count == 0)
        {
            throw new ArgumentException("At least one destination airport is required.");
        }

        if (request.ReturnDateFrom is not null || request.ReturnDateTo is not null)
        {
            throw new ArgumentException("Return flights are not supported yet.");
        }

        if (request.Adults <= 0)
        {
            throw new ArgumentException("At least one adult passenger is required.");
        }

        if (expandedCombinationCount <= 0)
        {
            throw new ArgumentException("Search must contain at least one valid origin, destination, and departure date combination.");
        }

        if (expandedCombinationCount > MaxSearchCombinations)
        {
            throw new ArgumentException($"Search exceeds the limit of {MaxSearchCombinations} combinations.");
        }
    }

    private static IEnumerable<ProviderSearchRequest> ExpandOneWaySearches(SearchRequest request)
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
                        request.CabinClass);
                }
            }
        }
    }

    private static IEnumerable<SearchFareOption> MapToSearchFareOptions(FlightApiOneWayResponse response)
    {
        var legsById = response.Legs.ToDictionary(leg => leg.Id);
        var segmentsById = response.Segments.ToDictionary(segment => segment.Id);
        var placesById = response.Places.ToDictionary(place => place.Id);
        var carriersById = response.Carriers.ToDictionary(carrier => carrier.Id);
        var agentsById = response.Agents
            .Where(agent => !string.IsNullOrWhiteSpace(agent.Id))
            .ToDictionary(agent => agent.Id, StringComparer.OrdinalIgnoreCase);

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
                .SelectMany(pricingOption => MapPricingItems(itinerary, pricingOption, resultLegs, agentsById));

            foreach (var result in pricingItems)
            {
                yield return result;
            }
        }
    }

    private static IEnumerable<SearchFareOption> MapPricingItems(
        FlightApiItinerary itinerary,
        FlightApiPricingOption pricingOption,
        List<SearchResultLeg> resultLegs,
        IReadOnlyDictionary<string, FlightApiAgent> agentsById)
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
                BuildDeepLink(itinerary.DeepLink),
                resultLegs);
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
                BuildDeepLink(item.Url ?? itinerary.DeepLink),
                resultLegs,
                agentName);
        }
    }

    private static SearchFareOption BuildSearchFareOption(
        string id,
        string flightId,
        decimal amount,
        string deepLink,
        List<SearchResultLeg> legs,
        string? agentName = null) =>
        new(
            id,
            flightId,
            string.IsNullOrWhiteSpace(agentName) ? ProviderName : $"{ProviderName}:{agentName}",
            false,
            new SearchResultPrice(amount, "EUR"),
            legs,
            legs.Sum(leg => leg.DurationMinutes),
            deepLink);

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

        return new SearchResultLeg(
            ResolvePlaceCode(leg.OriginPlaceId, placesById),
            ResolvePlaceCode(leg.DestinationPlaceId, placesById),
            DateTime.SpecifyKind(leg.Departure, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(leg.Arrival, DateTimeKind.Unspecified),
            leg.Duration,
            segments);
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

    private static StopCounts BuildStopCounts(IEnumerable<SearchResult> results)
    {
        var direct = 0;
        var oneStop = 0;
        var twoPlusStops = 0;

        foreach (var result in results)
        {
            var maxStopsOnAnyLeg = result.Legs.Count == 0
                ? 0
                : result.Legs.Max(leg => Math.Max(leg.Segments.Count - 1, 0));

            if (maxStopsOnAnyLeg == 0)
            {
                direct += 1;
                continue;
            }

            if (maxStopsOnAnyLeg == 1)
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
        SearchResultsQuery query)
    {
        var providers = query.GetProviders();
        var airlines = query.GetAirlines();
        var departureAirports = query.GetDepartureAirports();
        var arrivalAirports = query.GetArrivalAirports();
        var departureTimeRange = query.GetDepartureTimeRange();
        var arrivalTimeRange = query.GetArrivalTimeRange();

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
            .Where(result => MatchesStopFilters(result, query))
            .Where(result => MatchesAirlineFilters(result, airlines))
            .Where(result => MatchesAirportFilters(result, departureAirports, arrivalAirports))
            .Where(result => MatchesTimeFilters(result, departureTimeRange, arrivalTimeRange))
            .ToList();
    }

    private static List<SearchResult> ApplyFinalFilters(
        IEnumerable<SearchResult> results,
        SearchResultsQuery query)
        => results
            .Where(result => !query.MaxDuration.HasValue || result.TotalDurationMinutes <= query.MaxDuration.Value)
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

    private static SearchFiltersMetadata BuildGlobalFilterMetadata(List<SearchResult> results) =>
        new(
            BuildProviderCounts(results),
            BuildAirlineCounts(results),
            BuildAirportCounts(results, result => result.Legs.FirstOrDefault()?.OriginAirport),
            BuildAirportCounts(results, result => result.Legs.LastOrDefault()?.DestinationAirport),
            BuildDurationRange(results),
            BuildTimeRange(results, leg => leg.DepartureLocalTime),
            BuildTimeRange(results, leg => leg.ArrivalLocalTime),
            BuildStopFilterMetadata(results));

    private static SearchFiltersMetadata BuildFiltersMetadata(
        SearchFiltersMetadata globalFilters,
        List<SearchResult> baseFilteredResults) =>
        globalFilters with
        {
            DurationMinutes = BuildDurationRange(baseFilteredResults),
            DepartureTimeMinutes = BuildTimeRange(baseFilteredResults, leg => leg.DepartureLocalTime),
            ArrivalTimeMinutes = BuildTimeRange(baseFilteredResults, leg => leg.ArrivalLocalTime)
        };

    private static List<SearchFilterOptionCount> BuildProviderCounts(IEnumerable<SearchResult> results) =>
        results
            .SelectMany(result => result.PriceOptions)
            .GroupBy(option => option.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SearchFilterOptionCount(group.Key, group.Count()))
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<SearchFilterOptionCount> BuildAirlineCounts(IEnumerable<SearchResult> results) =>
        results
            .SelectMany(result => result.Legs)
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

    private static SearchRangeMetadata BuildDurationRange(IEnumerable<SearchResult> results)
    {
        var durations = results.Select(result => result.TotalDurationMinutes).ToList();
        if (durations.Count == 0)
        {
            return new SearchRangeMetadata(0, 0);
        }

        return new SearchRangeMetadata(durations.Min(), durations.Max());
    }

    private static SearchRangeMetadata BuildTimeRange(
        IEnumerable<SearchResult> results,
        Func<SearchResultLeg, DateTime> selector)
    {
        var minutes = results
            .SelectMany(result => result.Legs)
            .Select(leg => GetClockMinutes(selector(leg)))
            .ToList();

        if (minutes.Count == 0)
        {
            return new SearchRangeMetadata(0, 0);
        }

        return new SearchRangeMetadata(minutes.Min(), minutes.Max());
    }

    private static SearchStopFilterMetadata BuildStopFilterMetadata(IEnumerable<SearchResult> results)
    {
        var stopCounts = BuildStopCounts(results);
        return new SearchStopFilterMetadata(
            stopCounts.DirectFlightCount,
            stopCounts.OneStopFlightCount,
            stopCounts.TwoPlusStopFlightCount);
    }

    private static bool MatchesStopFilters(SearchResult result, SearchResultsQuery query)
    {
        if (!query.HasStopFilter)
        {
            return true;
        }

        var stopCount = result.Legs.Count == 0
            ? 0
            : result.Legs.Max(leg => Math.Max(leg.Segments.Count - 1, 0));

        return stopCount switch
        {
            0 => query.Direct ?? false,
            1 => query.OneStop ?? false,
            _ => query.TwoPlusStop ?? false
        };
    }

    private static bool MatchesAirlineFilters(SearchResult result, List<string> airlines)
    {
        if (airlines.Count == 0)
        {
            return true;
        }

        return result.Legs
            .SelectMany(leg => leg.Segments)
            .Any(segment => airlines.Contains(segment.MarketingCarrierName, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesAirportFilters(
        SearchResult result,
        List<string> departureAirports,
        List<string> arrivalAirports)
    {
        var departureAirport = result.Legs.FirstOrDefault()?.OriginAirport;
        var arrivalAirport = result.Legs.LastOrDefault()?.DestinationAirport;

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
        (int Min, int Max)? arrivalTimeRange)
    {
        var firstLeg = result.Legs.FirstOrDefault();
        var lastLeg = result.Legs.LastOrDefault();
        if (firstLeg is null || lastLeg is null)
        {
            return false;
        }

        var departureMinutes = GetClockMinutes(firstLeg.DepartureLocalTime);
        var arrivalMinutes = GetClockMinutes(lastLeg.ArrivalLocalTime);

        var matchesDeparture = !departureTimeRange.HasValue ||
            (departureMinutes >= departureTimeRange.Value.Min && departureMinutes <= departureTimeRange.Value.Max);
        var matchesArrival = !arrivalTimeRange.HasValue ||
            (arrivalMinutes >= arrivalTimeRange.Value.Min && arrivalMinutes <= arrivalTimeRange.Value.Max);

        return matchesDeparture && matchesArrival;
    }

    private static int GetClockMinutes(DateTime dateTime) => (dateTime.Hour * 60) + dateTime.Minute;

    private sealed record SearchFareOption(
        string Id,
        string FlightId,
        string Provider,
        bool IsRoundTrip,
        SearchResultPrice TotalPrice,
        List<SearchResultLeg> Legs,
        int TotalDurationMinutes,
        string DeepLink);

    private sealed record StopCounts(
        int DirectFlightCount,
        int OneStopFlightCount,
        int TwoPlusStopFlightCount);
}
