using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace backend.Features.ItinerarySearch;

public sealed class OptimizedItinerarySearchRunner(
    IServiceScopeFactory scopeFactory,
    IItinerarySearchSessionStore store,
    IProviderResponseCache providerResponseCache,
    IFlightApiRequestGate requestGate,
    IOptions<MultiDestinationSearchOptions> options,
    ILogger<OptimizedItinerarySearchRunner> logger,
    IOptions<FlightApiOptions>? flightApiOptions = null) : IOptimizedItinerarySearchRunner
{
    private readonly MultiDestinationSearchOptions _options = options.Value;
    private readonly string _currency = flightApiOptions?.Value.Currency ?? "EUR";

    public async Task RunAsync(
        string searchId,
        OptimizedTripRequest request,
        OptimizedSchedulePlan schedulePlan,
        int providerCallLimit,
        CancellationToken cancellationToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        var execution = new OptimizerExecution(searchId, request, schedulePlan, providerCallLimit, _options);
        try
        {
            await PersistAsync(execution.Snapshot("running", "searchingEdges", 0), cancellationToken);
            for (var index = 0; index < schedulePlan.Schedules.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (execution.StateLimitReached) break;

                var complete = await SearchScheduleAsync(execution, schedulePlan.Schedules[index], cancellationToken);
                execution.AddResults(complete);
                var progress = schedulePlan.Schedules.Count == 0
                    ? 95
                    : Math.Min(95, (int)Math.Floor((index + 1) * 95d / schedulePlan.Schedules.Count));
                await PersistAsync(execution.Snapshot("running", complete.Count > 0 ? "buildingItineraries" : "searchingEdges", progress), cancellationToken);
            }

            var status = execution.ProviderFailures > 0
                ? execution.Results.Count > 0 ? "partial" : "failed"
                : "completed";
            var completed = execution.Snapshot(status, "completed", 100);
            await PersistAsync(completed, cancellationToken);
            RecordFinished(completed, startedTimestamp);
        }
        catch (OperationCanceledException)
        {
            var current = await store.GetAsync(searchId, CancellationToken.None);
            if (current is not null && current.Status != "canceled")
            {
                execution.MarkTimedOut();
                var status = execution.Results.Count > 0 ? "partial" : "failed";
                var timedOut = execution.Snapshot(status, "timeout", current.Progress);
                await store.TrySetUnlessCanceledAsync(timedOut, CancellationToken.None);
                RecordFinished(timedOut, startedTimestamp);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Optimized itinerary search {SearchId} failed", searchId);
            execution.MarkUnexpectedFailure();
            var status = execution.Results.Count > 0 ? "partial" : "failed";
            var failed = execution.Snapshot(status, "failed", 100);
            await PersistAsync(failed, CancellationToken.None);
            RecordFinished(failed, startedTimestamp);
        }
    }

    private void RecordFinished(ItinerarySearchSessionResponse session, long startedTimestamp)
    {
        ItinerarySearchTelemetry.RecordFinished("optimize", session.Status, session.Coverage.Mode, session.Results.Count, session.Coverage.LiveProviderCallsUsed, startedTimestamp);
        logger.LogInformation(
            "Optimized itinerary search {SearchId} finished with status {SearchStatus}, coverage {CoverageMode}, {ResultCount} results, and {LiveProviderCallCount} live provider calls",
            session.SearchId,
            session.Status,
            session.Coverage.Mode,
            session.Results.Count,
            session.Coverage.LiveProviderCallsUsed);
    }

    private async Task<List<ItineraryResult>> SearchScheduleAsync(
        OptimizerExecution execution,
        AbstractItinerarySchedule schedule,
        CancellationToken cancellationToken)
    {
        var paths = new List<OptimizerPath> { OptimizerPath.Empty };
        for (var legIndex = 0; legIndex < schedule.Legs.Count; legIndex++)
        {
            var abstractLeg = schedule.Legs[legIndex];
            var next = new List<OptimizerPath>();
            var frontier = new PriorityQueue<OptimizerPath, decimal>();
            foreach (var path in paths) frontier.Enqueue(path, Priority(path, execution.Request.Ranking));
            while (frontier.TryDequeue(out var path, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (execution.StateLimitReached) break;

                var origins = Origins(execution.Request, abstractLeg, path);
                var destinations = Group(execution.Request, abstractLeg.ToGroupId).AirportCodes;
                foreach (var origin in origins)
                {
                    foreach (var destination in destinations)
                    {
                        if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase)) continue;
                        var providerRequest = new ProviderSearchRequest(
                            origin,
                            destination,
                            abstractLeg.DepartureDate,
                            execution.Request.Adults,
                            execution.Request.CabinClass,
                            _currency);
                        var fares = await GetEdgeAsync(execution, providerRequest, abstractLeg.Id, cancellationToken);
                        foreach (var fare in fares)
                        {
                            if (!IsTransitionValid(execution.Request, schedule, legIndex, path, fare))
                            {
                                execution.Prune();
                                continue;
                            }

                            if (!execution.TryEvaluateState()) break;
                            next.Add(path.Add(fare));
                        }

                        if (execution.StateLimitReached) break;
                    }
                    if (execution.StateLimitReached) break;
                }
            }

            paths = ParetoPrune(next, legIndex, execution).ToList();
            if (paths.Count == 0) return [];
        }

        return paths
            .Where(path => IsComplete(execution.Request, schedule, path))
            .Select(path => BuildResult(execution.Request, schedule, path))
            .ToList();
    }

    private async Task<List<OrderedFareCandidate>> GetEdgeAsync(
        OptimizerExecution execution,
        ProviderSearchRequest request,
        string legId,
        CancellationToken cancellationToken)
    {
        var cacheKey = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(request);
        if (execution.TryGetEdge(cacheKey, out var inMemory)) return FlightApiOrderedFareMapper.Map(inMemory, legId, request.Currency);

        try
        {
            var cached = await providerResponseCache.GetAsync<FlightApiOneWayResponse>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                requestGate.RecordCacheHit();
                execution.RecordCacheHit();
                execution.StoreEdge(cacheKey, cached);
                return FlightApiOrderedFareMapper.Map(cached, legId, request.Currency);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Optimizer cache lookup failed for {CacheKey}; treating the edge as a cache miss", cacheKey);
        }

        if (!execution.TryReserveLiveCall()) return [];
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();
            var response = await provider.SearchOneWayAsync(request, cancellationToken);
            execution.StoreEdge(cacheKey, response);
            return FlightApiOrderedFareMapper.Map(response, legId, request.Currency);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            execution.RecordProviderFailure();
            logger.LogWarning(exception, "Optimized FlightAPI edge failed for {Origin} to {Destination} on {Date}", request.OriginAirport, request.DestinationAirport, request.DepartureDate);
            execution.StoreEdge(cacheKey, new FlightApiOneWayResponse());
            return [];
        }
    }

    private static IEnumerable<string> Origins(OptimizedTripRequest request, AbstractScheduleLeg leg, OptimizerPath path)
    {
        if (path.Fares.Count == 0) return Group(request, leg.FromGroupId).AirportCodes;
        var continuity = Continuity(request, leg.FromGroupId);
        return continuity == "sameAirport"
            ? [path.Fares[^1].Leg.DestinationAirport]
            : Group(request, leg.FromGroupId).AirportCodes;
    }

    private static bool IsTransitionValid(
        OptimizedTripRequest request,
        AbstractItinerarySchedule schedule,
        int legIndex,
        OptimizerPath path,
        OrderedFareCandidate fare)
    {
        var abstractLeg = schedule.Legs[legIndex];
        if (DateOnly.FromDateTime(fare.Leg.DepartureLocalTime) != abstractLeg.DepartureDate) return false;
        if (abstractLeg.RequiredArrivalDate is { } required && DateOnly.FromDateTime(fare.Leg.ArrivalLocalTime) != required) return false;
        if (path.Fares.Count == 0) return true;

        var previous = path.Fares[^1].Leg;
        if (fare.Leg.DepartureLocalTime < previous.ArrivalLocalTime) return false;
        if (DateOnly.FromDateTime(fare.Leg.DepartureLocalTime) == DateOnly.FromDateTime(previous.DepartureLocalTime)) return false;
        var stay = request.Destinations.Single(destination => destination.Group.Id == abstractLeg.FromGroupId).Stay;
        return OptimizedScheduleGenerator.IsStaySatisfied(previous.ArrivalLocalTime, DateOnly.FromDateTime(fare.Leg.DepartureLocalTime), stay);
    }

    private static bool IsComplete(OptimizedTripRequest request, AbstractItinerarySchedule schedule, OptimizerPath path)
    {
        if (path.Fares.Count != schedule.Legs.Count) return false;
        if (request.EndpointMode != "openEnded") return true;
        var finalDestination = request.Destinations.Single(destination => destination.Group.Id == schedule.DestinationOrder[^1]);
        return OptimizedScheduleGenerator.IsStaySatisfied(path.Fares[^1].Leg.ArrivalLocalTime, request.EndDate, finalDestination.Stay);
    }

    private static IEnumerable<OptimizerPath> ParetoPrune(List<OptimizerPath> candidates, int legIndex, OptimizerExecution execution)
    {
        foreach (var group in candidates.GroupBy(path => $"{legIndex}:{path.Fares[^1].Leg.DestinationAirport}:{path.Fares[^1].Leg.ArrivalLocalTime.Ticks}"))
        {
            var frontier = new List<OptimizerPath>();
            foreach (var candidate in group.OrderBy(path => Priority(path, execution.Request.Ranking)))
            {
                if (frontier.Any(value => Dominates(value, candidate)))
                {
                    execution.Prune();
                    continue;
                }

                var removed = frontier.RemoveAll(value => Dominates(candidate, value));
                execution.Prune(removed);
                frontier.Add(candidate);
            }

            foreach (var candidate in frontier.Take(execution.Options.MaxCandidatesPerState)) yield return candidate;
            execution.Prune(Math.Max(0, frontier.Count - execution.Options.MaxCandidatesPerState));
        }
    }

    private static bool Dominates(OptimizerPath left, OptimizerPath right) =>
        left.Price <= right.Price && left.Duration <= right.Duration &&
        (left.Price < right.Price || left.Duration < right.Duration);

    private static decimal Priority(OptimizerPath path, string ranking) => ranking switch
    {
        "cheapest" => path.Price,
        "fastest" => path.Duration,
        _ => path.Price + path.Duration / 60m * 20m + path.Stops * 15m + Math.Max(path.Fares.Count - 1, 0) * 30m + path.Switches * 20m
    };

    private static ItineraryResult BuildResult(OptimizedTripRequest request, AbstractItinerarySchedule schedule, OptimizerPath path)
    {
        var stays = new List<ItineraryStay>();
        for (var index = 0; index < schedule.DestinationOrder.Count; index++)
        {
            var arrival = DateOnly.FromDateTime(path.Fares[index].Leg.ArrivalLocalTime);
            var departure = index + 1 < path.Fares.Count
                ? DateOnly.FromDateTime(path.Fares[index + 1].Leg.DepartureLocalTime)
                : request.EndDate;
            stays.Add(new(schedule.DestinationOrder[index], arrival, departure, OptimizedScheduleGenerator.StayNights(arrival, departure)));
        }

        var warnings = new List<ItineraryWarning>
        {
            new("separateTickets", "Each flight is booked separately; changes or disruption may need to be handled with multiple booking providers.")
        };
        if (path.Switches > 0) warnings.Add(new("airportSwitch", "This itinerary changes airports between separately booked flights; ground transfer is not included."));
        var id = $"{schedule.Id}|{string.Join("|", path.Fares.Select(fare => fare.Id))}";
        return new(
            id,
            "separateTickets",
            schedule.DestinationOrder,
            path.Fares.Select(fare => fare.Leg).ToList(),
            stays,
            path.Price,
            path.Fares[0].Currency,
            path.Duration,
            path.Stops,
            path.Fares.Count,
            path.Switches,
            path.Fares.Select(fare => fare.Booking).ToList(),
            warnings,
            new(0, path.Price, 0, path.Stops, Math.Max(path.Fares.Count - 1, 0), path.Switches));
    }

    private static AirportGroupRequest Group(OptimizedTripRequest request, string id)
    {
        if (request.Start.Id == id) return request.Start;
        if (request.FixedEnd?.Id == id) return request.FixedEnd;
        return request.Destinations.Single(destination => destination.Group.Id == id).Group;
    }

    private static string Continuity(OptimizedTripRequest request, string destinationId)
    {
        var configured = request.Destinations.Single(destination => destination.Group.Id == destinationId).AirportContinuity;
        return configured == "inherit" ? request.DefaultAirportContinuity : configured;
    }

    private Task PersistAsync(ItinerarySearchSessionResponse snapshot, CancellationToken cancellationToken) =>
        store.TrySetUnlessCanceledAsync(snapshot, cancellationToken);

    private sealed record OptimizerPath(List<OrderedFareCandidate> Fares)
    {
        public static OptimizerPath Empty { get; } = new([]);
        public decimal Price => Fares.Sum(fare => fare.Price);
        public int Duration => Fares.Sum(fare => fare.Leg.DurationMinutes);
        public int Stops => Fares.Sum(fare => fare.Leg.Stops);
        public int Switches => Fares.Zip(Fares.Skip(1)).Count(pair => !string.Equals(pair.First.Leg.DestinationAirport, pair.Second.Leg.OriginAirport, StringComparison.OrdinalIgnoreCase));
        public OptimizerPath Add(OrderedFareCandidate fare) => new([.. Fares, fare]);
    }

    private sealed class OptimizerExecution(
        string searchId,
        OptimizedTripRequest request,
        OptimizedSchedulePlan schedulePlan,
        int providerCallLimit,
        MultiDestinationSearchOptions options)
    {
        private readonly Dictionary<string, FlightApiOneWayResponse> _edges = new(StringComparer.Ordinal);
        private bool _providerBudgetReached;
        private bool _timedOut;
        private bool _unexpectedFailure;
        private int _liveCalls;
        private int _cacheHits;
        private int _evaluated;
        private int _pruned = schedulePlan.CandidateStatesPruned;

        public string SearchId { get; } = searchId;
        public OptimizedTripRequest Request { get; } = request;
        public OptimizedSchedulePlan SchedulePlan { get; } = schedulePlan;
        public int ProviderCallLimit { get; } = providerCallLimit;
        public MultiDestinationSearchOptions Options { get; } = options;
        public List<ItineraryResult> Results { get; private set; } = [];
        public int ProviderFailures { get; private set; }
        public bool StateLimitReached { get; private set; }

        public bool TryGetEdge(string key, out FlightApiOneWayResponse response) => _edges.TryGetValue(key, out response!);
        public void StoreEdge(string key, FlightApiOneWayResponse response) => _edges[key] = response;
        public void RecordCacheHit() => _cacheHits++;
        public void RecordProviderFailure() => ProviderFailures++;
        public void MarkTimedOut() => _timedOut = true;
        public void MarkUnexpectedFailure() => _unexpectedFailure = true;
        public void Prune(int count = 1) => _pruned += count;

        public bool TryReserveLiveCall()
        {
            if (_liveCalls >= ProviderCallLimit)
            {
                _providerBudgetReached = true;
                return false;
            }
            _liveCalls++;
            return true;
        }

        public bool TryEvaluateState()
        {
            if (_evaluated >= Options.MaxActiveStates)
            {
                StateLimitReached = true;
                return false;
            }
            _evaluated++;
            return true;
        }

        public void AddResults(List<ItineraryResult> values)
        {
            if (values.Count == 0) return;
            var raw = Results.Concat(values).DistinctBy(result => result.Id).ToList();
            var fastest = raw.Min(result => result.TotalFlightDurationMinutes);
            var cheapest = raw.Min(result => result.TotalPrice);
            var timeValuePerHour = Math.Clamp(cheapest * 0.10m, 10m, 30m);
            var ranked = raw.Select(result => result with
            {
                RankingBreakdown = result.RankingBreakdown with
                {
                    AdditionalFlightMinutes = result.TotalFlightDurationMinutes - fastest,
                    AdditionalBookings = Math.Max(result.BookingCount - 1, 0),
                    Score = result.TotalPrice
                        + (result.TotalFlightDurationMinutes - fastest) / 60m * timeValuePerHour
                        + result.TotalStops * timeValuePerHour * 0.75m
                        + Math.Max(result.BookingCount - 1, 0) * timeValuePerHour * 1.50m
                        + result.AirportSwitches * timeValuePerHour
                }
            }).ToList();
            var quota = Math.Max(1, Options.MaxStoredResults / 3);
            var recommended = ranked.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal);
            var cheapestFirst = ranked.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.Id, StringComparer.Ordinal);
            var fastestFirst = ranked.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal);
            var retained = recommended.Take(quota).Concat(cheapestFirst.Take(quota)).Concat(fastestFirst.Take(quota)).Concat(recommended)
                .DistinctBy(result => result.Id)
                .Take(Options.MaxStoredResults);
            Results = (Request.Ranking switch
            {
                "cheapest" => retained.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes),
                "fastest" => retained.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice),
                _ => retained.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice)
            }).ThenBy(result => result.Id, StringComparer.Ordinal).ToList();
        }

        public ItinerarySearchSessionResponse Snapshot(string status, string phase, int progress)
        {
            var bounded = SchedulePlan.Feasibility.Bounded || _providerBudgetReached || StateLimitReached || _timedOut || ProviderFailures > 0 || _unexpectedFailure;
            var warnings = new List<ItineraryWarning>();
            if (_providerBudgetReached) warnings.Add(new("providerBudgetReached", $"The optimizer reached its {ProviderCallLimit}-call live provider budget and continued with cached edges."));
            if (StateLimitReached) warnings.Add(new("stateBudgetReached", $"The optimizer reached its {Options.MaxActiveStates}-state limit."));
            if (ProviderFailures > 0) warnings.Add(new("providerCallsFailed", $"{ProviderFailures} flight-edge searches failed; complete itineraries from successful edges are still shown."));
            if (_timedOut) warnings.Add(new("executionTimeout", "The optimizer reached its execution timeout; complete itineraries found so far are shown."));
            if (_unexpectedFailure) warnings.Add(new("optimizerFailure", "The optimizer stopped unexpectedly; complete itineraries found so far are shown."));
            if (status is "completed" or "partial" or "failed" && Results.Count == 0) warnings.Add(new("noCompleteItinerary", "No complete itinerary could be built from the evaluated flight edges."));
            return new(
                SearchId,
                "optimize",
                status,
                phase,
                progress,
                new(bounded ? "bounded" : "exhaustive", _liveCalls, ProviderCallLimit, _cacheHits, Math.Min(SchedulePlan.CandidateStatesEvaluated + _evaluated, Options.MaxActiveStates), _pruned),
                Results,
                warnings,
                status == "failed" ? "The optimizer could not produce a complete itinerary." : null,
                Feasibility: SchedulePlan.Feasibility,
                AbstractSchedules: SchedulePlan.Schedules);
        }
    }
}
