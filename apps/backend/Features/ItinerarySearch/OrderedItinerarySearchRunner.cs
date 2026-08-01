using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace backend.Features.ItinerarySearch;

public sealed class OrderedItinerarySearchRunner(
    IServiceScopeFactory scopeFactory,
    IItinerarySearchSessionStore store,
    IProviderResponseCache providerResponseCache,
    IFlightApiRequestGate requestGate,
    IOptions<MultiDestinationSearchOptions> options,
    ILogger<OrderedItinerarySearchRunner> logger,
    IOptions<backend.Infrastructure.Providers.FlightApi.Models.FlightApiOptions>? flightApiOptions = null) : IOrderedItinerarySearchRunner
{
    private readonly string _currency = flightApiOptions?.Value.Currency ?? "EUR";
    public async Task RunAsync(string searchId, OrderedTripRequest request, int providerCallLimit, CancellationToken cancellationToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        var allRequests = Expand(request, _currency).ToList();
        var cached = new List<CachedOrderedProviderRequest>();
        var cacheMisses = new List<OrderedProviderRequest>();
        foreach (var item in allRequests)
        {
            var cacheKey = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(item.Request);
            backend.Infrastructure.Providers.FlightApi.Models.FlightApiOneWayResponse? response = null;
            try
            {
                response = await providerResponseCache.GetAsync<backend.Infrastructure.Providers.FlightApi.Models.FlightApiOneWayResponse>(cacheKey, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var current = await store.GetAsync(searchId, CancellationToken.None);
                if (current is not null && current.Status != "canceled")
                {
                    var timedOut = current with { Status = "failed", Phase = "timeout", ErrorMessage = "The ordered search exceeded its execution timeout." };
                    await store.TrySetUnlessCanceledAsync(timedOut, CancellationToken.None);
                    RecordFinished(timedOut, startedTimestamp);
                }
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Ordered itinerary cache lookup failed for {CacheKey}; treating the edge as a cache miss", cacheKey);
            }
            if (response is null) cacheMisses.Add(item);
            else cached.Add(new(item, response));
        }
        var cacheCoveredLegs = cached
            .Where(item => FlightApiOrderedFareMapper.Map(item.Response, item.Request.LegId, item.Request.Request.Currency).Count > 0)
            .Select(item => item.Request.LegIndex)
            .ToHashSet();
        var selected = SelectWithinBudget(cacheMisses, providerCallLimit, cacheCoveredLegs);
        var state = new OrderedExecutionState(request, cached.Count, selected.Count, cacheMisses.Count, providerCallLimit, options.Value);
        try
        {
            await PersistAsync(searchId, state.Snapshot(searchId, "running", "searching"), cancellationToken);
            await Task.WhenAll(
                cached.Select(item => ExecuteCachedAsync(searchId, item, state, cancellationToken))
                    .Concat(selected.Select(item => ExecuteAsync(searchId, item, state, cancellationToken))));
            var final = state.Snapshot(searchId, "completed", "completed", forceProgress: 100);
            await PersistAsync(searchId, final, cancellationToken);
            RecordFinished(final, startedTimestamp);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is persisted by the service. A timeout is surfaced if it did not originate there.
            var current = await store.GetAsync(searchId, CancellationToken.None);
            if (current is not null && current.Status != "canceled")
            {
                var timedOut = current with { Status = "failed", Phase = "timeout", ErrorMessage = "The ordered search exceeded its execution timeout." };
                await store.TrySetUnlessCanceledAsync(timedOut, CancellationToken.None);
                RecordFinished(timedOut, startedTimestamp);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ordered itinerary search {SearchId} failed", searchId);
            var failed = state.Snapshot(searchId, "failed", "failed") with { ErrorMessage = "The ordered search failed before completion." };
            await PersistAsync(searchId, failed, CancellationToken.None);
            RecordFinished(failed, startedTimestamp);
        }
    }

    private void RecordFinished(ItinerarySearchSessionResponse session, long startedTimestamp)
    {
        ItinerarySearchTelemetry.RecordFinished("ordered", session.Status, session.Coverage.Mode, session.Results.Count, session.Coverage.LiveProviderCallsUsed, startedTimestamp);
        logger.LogInformation(
            "Ordered itinerary search {SearchId} finished with status {SearchStatus}, coverage {CoverageMode}, {ResultCount} results, and {LiveProviderCallCount} live provider calls",
            session.SearchId,
            session.Status,
            session.Coverage.Mode,
            session.Results.Count,
            session.Coverage.LiveProviderCallsUsed);
    }

    private async Task ExecuteCachedAsync(string searchId, CachedOrderedProviderRequest item, OrderedExecutionState state, CancellationToken cancellationToken)
    {
        requestGate.RecordCacheHit();
        state.Complete(item.Request.LegIndex, FlightApiOrderedFareMapper.Map(item.Response, item.Request.LegId, item.Request.Request.Currency), failed: false, cacheHit: true);
        await PersistAsync(searchId, state.Snapshot(searchId, "running", "searching"), cancellationToken);
    }

    private async Task ExecuteAsync(string searchId, OrderedProviderRequest item, OrderedExecutionState state, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();
            var response = await provider.SearchOneWayAsync(item.Request, cancellationToken);
            state.Complete(item.LegIndex, FlightApiOrderedFareMapper.Map(response, item.LegId, item.Request.Currency), failed: false, cacheHit: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ordered FlightAPI edge failed for {Origin} to {Destination} on {Date}", item.Request.OriginAirport, item.Request.DestinationAirport, item.Request.DepartureDate);
            state.Complete(item.LegIndex, [], failed: true, cacheHit: false);
        }

        await PersistAsync(searchId, state.Snapshot(searchId, "running", "searching"), cancellationToken);
    }

    private async Task PersistAsync(string searchId, ItinerarySearchSessionResponse snapshot, CancellationToken cancellationToken)
    {
        await store.TrySetUnlessCanceledAsync(snapshot, cancellationToken);
    }

    private static IEnumerable<OrderedProviderRequest> Expand(OrderedTripRequest request, string currency)
    {
        for (var index = 0; index < request.Legs.Count; index++)
        {
            var leg = request.Legs[index];
            foreach (var origin in leg.From.AirportCodes)
                foreach (var destination in leg.To.AirportCodes)
                {
                    if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase)) continue;
                    yield return new(index, leg.Id, new(origin, destination, leg.DepartureDate, request.Adults, request.CabinClass, currency));
                }
        }
    }

    private static List<OrderedProviderRequest> SelectWithinBudget(List<OrderedProviderRequest> requests, int limit, HashSet<int> cacheCoveredLegs)
    {
        var groups = requests.GroupBy(request => request.LegIndex).OrderBy(group => group.Key).ToList();
        var selected = groups.Where(group => !cacheCoveredLegs.Contains(group.Key))
            .Concat(groups.Where(group => cacheCoveredLegs.Contains(group.Key)))
            .Select(group => group.First())
            .Take(limit)
            .ToList();
        if (selected.Count == limit) return selected;
        selected.AddRange(requests.Where(request => !selected.Contains(request)).Take(limit - selected.Count));
        return selected;
    }

    private sealed record OrderedProviderRequest(int LegIndex, string LegId, ProviderSearchRequest Request);
    private sealed record CachedOrderedProviderRequest(OrderedProviderRequest Request, backend.Infrastructure.Providers.FlightApi.Models.FlightApiOneWayResponse Response);
}

internal sealed class OrderedExecutionState
{
    private readonly object _sync = new();
    private readonly OrderedTripRequest _request;
    private readonly List<OrderedFareCandidate>[] _fares;
    private readonly int _scheduledOperations;
    private readonly int _selectedLiveCalls;
    private readonly int _plannedLiveCalls;
    private readonly int _providerCallLimit;
    private readonly MultiDestinationSearchOptions _options;
    private int _completed;
    private int _completedLiveCalls;
    private int _cacheHits;
    private int _failed;

    public OrderedExecutionState(OrderedTripRequest request, int cachedCalls, int selectedLiveCalls, int plannedLiveCalls, int providerCallLimit, MultiDestinationSearchOptions options)
    {
        _request = request;
        _scheduledOperations = cachedCalls + selectedLiveCalls;
        _selectedLiveCalls = selectedLiveCalls;
        _plannedLiveCalls = plannedLiveCalls;
        _providerCallLimit = providerCallLimit;
        _options = options;
        _fares = Enumerable.Range(0, request.Legs.Count).Select(_ => new List<OrderedFareCandidate>()).ToArray();
    }

    public void Complete(int legIndex, List<OrderedFareCandidate> fares, bool failed, bool cacheHit)
    {
        lock (_sync)
        {
            _fares[legIndex].AddRange(fares);
            _completed++;
            if (cacheHit) _cacheHits++;
            else _completedLiveCalls++;
            if (failed) _failed++;
        }
    }

    public ItinerarySearchSessionResponse Snapshot(string searchId, string status, string phase, int? forceProgress = null)
    {
        lock (_sync)
        {
            var results = BuildResults();
            var warnings = new List<ItineraryWarning>();
            if (_plannedLiveCalls > _selectedLiveCalls) warnings.Add(new("providerBudgetReached", $"Searched {_selectedLiveCalls} of {_plannedLiveCalls} uncached airport pairs because the live provider-call budget was reached."));
            if (_failed > 0) warnings.Add(new("providerCallsFailed", $"{_failed} airport-pair searches failed; available complete itineraries are still shown."));
            if (status == "completed" && results.Count == 0) warnings.Add(new("noCompleteItinerary", "No complete itinerary could be built from the available fares."));
            var progress = forceProgress ?? (_scheduledOperations == 0 ? 100 : (int)Math.Floor(_completed * 100d / _scheduledOperations));
            return new(
                searchId, "ordered", status, phase, progress,
                new(_plannedLiveCalls > _selectedLiveCalls ? "bounded" : "exhaustive", _completedLiveCalls, _providerCallLimit, _cacheHits, results.Count, 0),
                results, warnings);
        }
    }

    private List<ItineraryResult> BuildResults()
    {
        if (_fares.Any(fares => fares.Count == 0)) return [];
        var paths = _fares[0].Select(fare => new OrderedPath([fare])).ToList();
        for (var legIndex = 1; legIndex < _fares.Length; legIndex++)
        {
            var continuity = _request.Legs[legIndex].AirportContinuityWithPrevious;
            var expanded = from path in paths
                           from fare in _fares[legIndex]
                           where continuity == "allowSwitch" || string.Equals(path.Fares[^1].Leg.DestinationAirport, fare.Leg.OriginAirport, StringComparison.OrdinalIgnoreCase)
                           where fare.Leg.DepartureLocalTime >= path.Fares[^1].Leg.ArrivalLocalTime
                           select new OrderedPath([.. path.Fares, fare]);
            paths = Trim(expanded).ToList();
            if (paths.Count == 0) return [];
        }

        var raw = paths.Select(BuildResult).ToList();
        var fastest = raw.Min(result => result.TotalFlightDurationMinutes);
        var cheapest = raw.Min(result => result.TotalPrice);
        var timeValuePerHour = Math.Clamp(cheapest * 0.10m, 10m, 30m);
        var ranked = raw.Select(result =>
        {
            var additionalMinutes = result.TotalFlightDurationMinutes - fastest;
            var additionalBookings = Math.Max(result.BookingCount - 1, 0);
            var breakdown = result.RankingBreakdown with
            {
                AdditionalFlightMinutes = additionalMinutes,
                AdditionalBookings = additionalBookings,
                Score = result.TotalPrice
                    + additionalMinutes / 60m * timeValuePerHour
                    + result.TotalStops * timeValuePerHour * 0.75m
                    + additionalBookings * timeValuePerHour * 1.50m
                    + result.AirportSwitches * timeValuePerHour
            };
            return result with { RankingBreakdown = breakdown };
        }).ToList();
        var recommended = ranked.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal).ToList();
        var cheapestFirst = ranked.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.Id, StringComparer.Ordinal).ToList();
        var fastestFirst = ranked.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal).ToList();
        var quota = Math.Max(1, _options.MaxStoredResults / 3);
        var retained = recommended.Take(quota)
            .Concat(cheapestFirst.Take(quota))
            .Concat(fastestFirst.Take(quota))
            .Concat(recommended)
            .DistinctBy(result => result.Id)
            .Take(_options.MaxStoredResults)
            .ToList();
        return (_request.Ranking switch
        {
            "cheapest" => retained.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes),
            "fastest" => retained.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice),
            _ => retained.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice)
        }).ThenBy(result => result.Id, StringComparer.Ordinal).ToList();
    }

    private IEnumerable<OrderedPath> Trim(IEnumerable<OrderedPath> paths)
    {
        var quota = Math.Max(1, _options.MaxCandidatesPerState / 3);
        return paths.GroupBy(path => path.Fares[^1].Leg.DestinationAirport, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var values = group.ToList();
                var fastestDuration = values.Min(path => path.Duration);
                var lowestPrice = values.Min(path => path.Price);
                var timeValuePerHour = Math.Clamp(lowestPrice * 0.10m, 10m, 30m);
                var recommended = values.OrderBy(path =>
                        path.Price
                        + (path.Duration - fastestDuration) / 60m * timeValuePerHour
                        + path.Stops * timeValuePerHour * 0.75m
                        + Math.Max(path.Fares.Count - 1, 0) * timeValuePerHour * 1.50m
                        + path.Switches * timeValuePerHour)
                    .ThenBy(path => path.Id, StringComparer.Ordinal);
                var cheapest = values.OrderBy(path => path.Price).ThenBy(path => path.Duration).ThenBy(path => path.Id, StringComparer.Ordinal);
                var fastest = values.OrderBy(path => path.Duration).ThenBy(path => path.Price).ThenBy(path => path.Id, StringComparer.Ordinal);
                return recommended.Take(quota).Concat(cheapest.Take(quota)).Concat(fastest.Take(quota)).Concat(recommended)
                    .DistinctBy(path => path.Id)
                    .Take(_options.MaxCandidatesPerState);
            })
            .Take(_options.MaxActiveStates);
    }

    private static ItineraryResult BuildResult(OrderedPath path)
    {
        var warnings = path.Switches == 0 ? new List<ItineraryWarning>() : [new("airportSwitch", "This itinerary changes airports between separately booked flights.")];
        return new(
            path.Id, "separateTickets", path.Fares.Select(fare => fare.Leg.DestinationAirport).ToList(), path.Fares.Select(fare => fare.Leg).ToList(), [],
            path.Price, path.Fares[0].Currency, path.Duration, path.Stops, path.Fares.Count, path.Switches,
            path.Fares.Select(fare => fare.Booking).ToList(), warnings,
            new(0, path.Price, 0, path.Stops, path.Fares.Count - 1, path.Switches));
    }

    private sealed record OrderedPath(List<OrderedFareCandidate> Fares)
    {
        public string Id => string.Join("|", Fares.Select(fare => fare.Id));
        public decimal Price => Fares.Sum(fare => fare.Price);
        public int Duration => Fares.Sum(fare => fare.Leg.DurationMinutes);
        public int Stops => Fares.Sum(fare => fare.Leg.Stops);
        public int Switches => Fares.Zip(Fares.Skip(1)).Count(pair => !string.Equals(pair.First.Leg.DestinationAirport, pair.Second.Leg.OriginAirport, StringComparison.OrdinalIgnoreCase));
    }
}
