using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace backend.Features.ItinerarySearch;

public sealed class OrderedItinerarySearchRunner(
    IServiceScopeFactory scopeFactory,
    IItinerarySearchSessionStore store,
    IOptions<MultiDestinationSearchOptions> options,
    ILogger<OrderedItinerarySearchRunner> logger) : IOrderedItinerarySearchRunner
{
    public async Task RunAsync(string searchId, OrderedTripRequest request, int providerCallLimit, CancellationToken cancellationToken)
    {
        var allRequests = Expand(request).ToList();
        var selected = SelectWithinBudget(allRequests, providerCallLimit);
        var state = new OrderedExecutionState(request, selected.Count, allRequests.Count, options.Value);
        try
        {
            await PersistAsync(searchId, state.Snapshot(searchId, "running", "searching"), cancellationToken);
            await Task.WhenAll(selected.Select(item => ExecuteAsync(searchId, item, state, cancellationToken)));
            var final = state.Snapshot(searchId, "completed", "completed", forceProgress: 100);
            await PersistAsync(searchId, final, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is persisted by the service. A timeout is surfaced if it did not originate there.
            var current = await store.GetAsync(searchId, CancellationToken.None);
            if (current is not null && current.Status != "canceled")
                await store.TrySetUnlessCanceledAsync(current with { Status = "failed", Phase = "timeout", ErrorMessage = "The ordered search exceeded its execution timeout." }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ordered itinerary search {SearchId} failed", searchId);
            var failed = state.Snapshot(searchId, "failed", "failed") with { ErrorMessage = "The ordered search failed before completion." };
            await PersistAsync(searchId, failed, CancellationToken.None);
        }
    }

    private async Task ExecuteAsync(string searchId, OrderedProviderRequest item, OrderedExecutionState state, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IFlightSearchProvider>();
            var response = await provider.SearchOneWayAsync(item.Request, cancellationToken);
            state.Complete(item.LegIndex, FlightApiOrderedFareMapper.Map(response, item.LegId), failed: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ordered FlightAPI edge failed for {Origin} to {Destination} on {Date}", item.Request.OriginAirport, item.Request.DestinationAirport, item.Request.DepartureDate);
            state.Complete(item.LegIndex, [], failed: true);
        }

        await PersistAsync(searchId, state.Snapshot(searchId, "running", "searching"), cancellationToken);
    }

    private async Task PersistAsync(string searchId, ItinerarySearchSessionResponse snapshot, CancellationToken cancellationToken)
    {
        await store.TrySetUnlessCanceledAsync(snapshot, cancellationToken);
    }

    private static IEnumerable<OrderedProviderRequest> Expand(OrderedTripRequest request)
    {
        for (var index = 0; index < request.Legs.Count; index++)
        {
            var leg = request.Legs[index];
            foreach (var origin in leg.From.AirportCodes)
                foreach (var destination in leg.To.AirportCodes)
                {
                    if (string.Equals(origin, destination, StringComparison.OrdinalIgnoreCase)) continue;
                    yield return new(index, leg.Id, new(origin, destination, leg.DepartureDate, request.Adults, request.CabinClass));
                }
        }
    }

    private static List<OrderedProviderRequest> SelectWithinBudget(List<OrderedProviderRequest> requests, int limit)
    {
        var selected = requests.GroupBy(request => request.LegIndex).OrderBy(group => group.Key).Select(group => group.First()).Take(limit).ToList();
        if (selected.Count == limit) return selected;
        selected.AddRange(requests.Where(request => !selected.Contains(request)).Take(limit - selected.Count));
        return selected;
    }

    private sealed record OrderedProviderRequest(int LegIndex, string LegId, ProviderSearchRequest Request);
}

internal sealed class OrderedExecutionState
{
    private readonly object _sync = new();
    private readonly OrderedTripRequest _request;
    private readonly List<OrderedFareCandidate>[] _fares;
    private readonly int _selectedCalls;
    private readonly int _plannedCalls;
    private readonly MultiDestinationSearchOptions _options;
    private int _completed;
    private int _failed;

    public OrderedExecutionState(OrderedTripRequest request, int selectedCalls, int plannedCalls, MultiDestinationSearchOptions options)
    {
        _request = request; _selectedCalls = selectedCalls; _plannedCalls = plannedCalls; _options = options;
        _fares = Enumerable.Range(0, request.Legs.Count).Select(_ => new List<OrderedFareCandidate>()).ToArray();
    }

    public void Complete(int legIndex, List<OrderedFareCandidate> fares, bool failed)
    {
        lock (_sync)
        {
            _fares[legIndex].AddRange(fares);
            _completed++;
            if (failed) _failed++;
        }
    }

    public ItinerarySearchSessionResponse Snapshot(string searchId, string status, string phase, int? forceProgress = null)
    {
        lock (_sync)
        {
            var results = BuildResults();
            var warnings = new List<ItineraryWarning>();
            if (_plannedCalls > _selectedCalls) warnings.Add(new("providerBudgetReached", $"Searched {_selectedCalls} of {_plannedCalls} airport pairs because the provider-call budget was reached."));
            if (_failed > 0) warnings.Add(new("providerCallsFailed", $"{_failed} airport-pair searches failed; available complete itineraries are still shown."));
            if (status == "completed" && results.Count == 0) warnings.Add(new("noCompleteItinerary", "No complete itinerary could be built from the available fares."));
            var progress = forceProgress ?? (_selectedCalls == 0 ? 100 : (int)Math.Floor(_completed * 100d / _selectedCalls));
            return new(
                searchId, "ordered", status, phase, progress,
                new(_plannedCalls > _selectedCalls ? "bounded" : "exhaustive", _completed, _selectedCalls, 0, results.Count, 0),
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
        var ranked = raw.Select(result =>
        {
            var breakdown = result.RankingBreakdown with
            {
                AdditionalFlightMinutes = result.TotalFlightDurationMinutes - fastest,
                Score = result.TotalPrice - cheapest + (result.TotalFlightDurationMinutes - fastest) / 60m * 12m + result.TotalStops * 15m + (result.BookingCount - 1) * 20m + result.AirportSwitches * 25m
            };
            return result with { RankingBreakdown = breakdown };
        });
        return (_request.Ranking switch
        {
            "cheapest" => ranked.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes),
            "fastest" => ranked.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice),
            _ => ranked.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice)
        }).ThenBy(result => result.Id, StringComparer.Ordinal).Take(_options.MaxStoredResults).ToList();
    }

    private IEnumerable<OrderedPath> Trim(IEnumerable<OrderedPath> paths)
    {
        var quota = Math.Max(1, _options.MaxCandidatesPerState / 3);
        return paths.GroupBy(path => path.Fares[^1].Leg.DestinationAirport, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var values = group.ToList();
                var recommended = values.OrderBy(path => path.Price + path.Duration / 60m * 12m + path.Stops * 15m + path.Switches * 25m).ThenBy(path => path.Id, StringComparer.Ordinal);
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
