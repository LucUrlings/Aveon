using backend.Features.ItinerarySearch.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace backend.Features.ItinerarySearch;

public sealed class ItinerarySearchService(
    IItinerarySearchSessionStore store,
    IOrderedItinerarySearchRunner orderedRunner,
    IOptimizedScheduleGenerator optimizedScheduleGenerator,
    IOptimizedItinerarySearchRunner optimizedRunner,
    Microsoft.Extensions.Options.IOptions<MultiDestinationSearchOptions> options,
    ILogger<ItinerarySearchService>? logger = null) : IItinerarySearchService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _executions = new();
    private readonly ILogger<ItinerarySearchService> _logger = logger ?? NullLogger<ItinerarySearchService>.Instance;

    public async Task<ItinerarySearchSessionResponse> StartAsync(ItinerarySearchRequest request, int providerCallLimit, CancellationToken cancellationToken)
    {
        ValidateShape(request);
        request = Normalize(request);
        Validate(request, options.Value);
        var schedulePlan = request is OptimizedTripRequest optimizedForPlan ? optimizedScheduleGenerator.Generate(optimizedForPlan) : null;
        var searchId = Guid.NewGuid().ToString("N");
        var mode = request is OptimizedTripRequest ? "optimize" : "ordered";
        var effectiveLimit = Math.Clamp(providerCallLimit, 1, options.Value.HardMaxProviderCalls);
        var session = new ItinerarySearchSessionResponse(
            searchId,
            mode,
            "running",
            "validating",
            0,
            new SearchCoverage(ProviderCallLimit: effectiveLimit),
            [],
            [],
            Feasibility: schedulePlan?.Feasibility,
            AbstractSchedules: schedulePlan?.Schedules);
        await store.SetAsync(session, cancellationToken);
        ItinerarySearchTelemetry.RecordStarted(mode);
        _logger.LogInformation(
            "Multi-destination search {SearchId} started in {SearchMode} mode with provider call limit {ProviderCallLimit}",
            searchId,
            mode,
            effectiveLimit);
        if (request is OrderedTripRequest ordered)
        {
            var execution = new CancellationTokenSource(TimeSpan.FromMinutes(options.Value.ExecutionTimeoutMinutes));
            _executions[searchId] = execution;
            _ = Task.Run(async () =>
            {
                try { await orderedRunner.RunAsync(searchId, ordered, effectiveLimit, execution.Token); }
                finally
                {
                    _executions.TryRemove(searchId, out _);
                    execution.Dispose();
                }
            }, CancellationToken.None);
        }
        else if (request is OptimizedTripRequest optimized && schedulePlan is not null)
        {
            var execution = new CancellationTokenSource(TimeSpan.FromMinutes(options.Value.ExecutionTimeoutMinutes));
            _executions[searchId] = execution;
            _ = Task.Run(async () =>
            {
                try
                {
                    await optimizedRunner.RunAsync(searchId, optimized, schedulePlan, effectiveLimit, execution.Token);
                }
                finally
                {
                    _executions.TryRemove(searchId, out _);
                    execution.Dispose();
                }
            }, CancellationToken.None);
        }
        return session;
    }
    public async Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, ItineraryResultsQuery query, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(searchId, cancellationToken);
        return session is null ? null : ItineraryResultFilter.Apply(session, query);
    }
    public async Task<ItinerarySearchSessionResponse?> CancelAsync(string searchId, CancellationToken cancellationToken)
    {
        var session = await store.GetAsync(searchId, cancellationToken);
        if (session is null) return null;
        if (session.Status is "completed" or "failed" or "canceled") return session;
        if (_executions.TryGetValue(searchId, out var execution)) execution.Cancel();
        session = session with { Status = "canceled", Phase = "canceled" };
        await store.SetAsync(session, cancellationToken);
        ItinerarySearchTelemetry.RecordCanceled(session.Mode);
        _logger.LogInformation("Multi-destination search {SearchId} canceled in {SearchMode} mode", searchId, session.Mode);
        return session;
    }
    private static void Validate(ItinerarySearchRequest request, MultiDestinationSearchOptions limits)
    {
        if (request.Adults is < 1 or > 9) throw Invalid("adults", "Must be between 1 and 9.");
        if (request.CabinClass is not ("economy" or "premium_economy" or "business" or "first")) throw Invalid("cabinClass", "Unsupported cabin class.");
        if (request.Ranking is not ("recommended" or "cheapest" or "fastest")) throw Invalid("ranking", "Must be recommended, cheapest, or fastest.");
        if (request is OrderedTripRequest ordered)
        {
            if (ordered.Legs.Count < 1 || ordered.Legs.Count > limits.MaxOrderedLegs) throw Invalid("legs", $"Must contain between 1 and {limits.MaxOrderedLegs} legs.");
            if (ordered.Legs.Select(leg => leg.Id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Legs.Count) throw Invalid("legs", "Leg IDs must be unique.");
            foreach (var leg in ordered.Legs)
            {
                ValidateGroup(leg.From, "legs.from", limits); ValidateGroup(leg.To, "legs.to", limits);
                if (leg.AirportContinuityWithPrevious is not ("sameAirport" or "allowSwitch")) throw Invalid("legs.airportContinuityWithPrevious", "Unsupported airport continuity.");
            }
            if (ordered.Legs.Zip(ordered.Legs.Skip(1)).Any(pair => pair.Second.DepartureDate < pair.First.DepartureDate)) throw Invalid("legs.departureDate", "Leg dates must be chronological.");
            return;
        }
        var optimized = (OptimizedTripRequest)request;
        ValidateGroup(optimized.Start, "start", limits);
        if (optimized.Destinations.Count < 1 || optimized.Destinations.Count > limits.MaxOptimizedDestinations) throw Invalid("destinations", $"Must contain between 1 and {limits.MaxOptimizedDestinations} destinations.");
        if (optimized.EndDate < optimized.StartDate || optimized.EndDate > optimized.StartDate.AddDays(limits.MaxTripDays - 1)) throw Invalid("endDate", $"Must be within {limits.MaxTripDays} calendar days of startDate.");
        if (optimized.EndpointMode is not ("returnToStart" or "openEnded" or "fixedEnd")) throw Invalid("endpointMode", "Unsupported endpoint mode.");
        if (optimized.EndpointMode == "fixedEnd" && optimized.FixedEnd is null) throw Invalid("fixedEnd", "Required for fixedEnd endpoint mode.");
        if (optimized.EndpointMode != "fixedEnd" && optimized.FixedEnd is not null) throw Invalid("fixedEnd", "Only allowed for fixedEnd endpoint mode.");
        if (optimized.FixedEnd is not null) ValidateGroup(optimized.FixedEnd, "fixedEnd", limits);
        if (optimized.DefaultAirportContinuity is not ("sameAirport" or "allowSwitch")) throw Invalid("defaultAirportContinuity", "Unsupported airport continuity.");
        var destinationIds = optimized.Destinations.Select(destination => destination.Group.Id).ToList();
        if (destinationIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinationIds.Count) throw Invalid("destinations", "Destination IDs must be unique.");
        if (optimized.FixedEnd is not null && optimized.Destinations.Any(destination => SameAirportGroup(destination.Group, optimized.FixedEnd)))
            throw Invalid("fixedEnd", "Must not duplicate an unordered destination airport group.");
        foreach (var destination in optimized.Destinations)
        {
            ValidateGroup(destination.Group, "destinations.group", limits);
            if (destination.Stay.Mode is not ("minimumNights" or "exactNights") || destination.Stay.Nights < 0) throw Invalid("destinations.stay", "Stay mode or nights are invalid.");
            if (destination.AirportContinuity is not ("inherit" or "sameAirport" or "allowSwitch")) throw Invalid("destinations.airportContinuity", "Unsupported airport continuity.");
        }
    }
    private static void ValidateGroup(AirportGroupRequest group, string field, MultiDestinationSearchOptions limits)
    {
        if (string.IsNullOrWhiteSpace(group.Id)) throw Invalid($"{field}.id", "Required.");
        if (string.IsNullOrWhiteSpace(group.Label)) throw Invalid($"{field}.label", "Required.");
        if (group.AirportCodes.Count < 1 || group.AirportCodes.Count > limits.MaxAirportsPerGroup) throw Invalid($"{field}.airportCodes", $"Must contain between 1 and {limits.MaxAirportsPerGroup} airports.");
        if (group.AirportCodes.Any(code => !System.Text.RegularExpressions.Regex.IsMatch(code.Trim(), "^[A-Za-z]{3}$"))) throw Invalid($"{field}.airportCodes", "Must contain three-letter IATA codes.");
        if (group.AirportCodes.Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != group.AirportCodes.Count) throw Invalid($"{field}.airportCodes", "Must not contain duplicate codes.");
    }
    private static ItineraryValidationException Invalid(string field, string message) => new(field, message);

    private static bool SameAirportGroup(AirportGroupRequest left, AirportGroupRequest right) =>
        left.AirportCodes.Count == right.AirportCodes.Count &&
        left.AirportCodes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(right.AirportCodes);

    private static void ValidateShape(ItinerarySearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CabinClass)) throw Invalid("cabinClass", "Required.");
        if (string.IsNullOrWhiteSpace(request.Ranking)) throw Invalid("ranking", "Required.");
        if (request is OrderedTripRequest ordered)
        {
            if (ordered.Legs is null) throw Invalid("legs", "Required.");
            if (ordered.Legs.Any(leg => leg is null || leg.From is null || leg.To is null)) throw Invalid("legs", "Every leg requires from and to airport groups.");
            foreach (var leg in ordered.Legs)
            {
                ValidateGroupShape(leg.From, "legs.from");
                ValidateGroupShape(leg.To, "legs.to");
            }
        }
        if (request is OptimizedTripRequest optimized)
        {
            if (optimized.Start is null) throw Invalid("start", "Required.");
            if (optimized.Destinations is null || optimized.Destinations.Any(destination => destination is null || destination.Group is null || destination.Stay is null)) throw Invalid("destinations", "Every destination requires a group and stay rule.");
            ValidateGroupShape(optimized.Start, "start");
            if (optimized.FixedEnd is not null) ValidateGroupShape(optimized.FixedEnd, "fixedEnd");
            foreach (var destination in optimized.Destinations) ValidateGroupShape(destination.Group, "destinations.group");
        }
    }

    private static void ValidateGroupShape(AirportGroupRequest group, string field)
    {
        if (group.AirportCodes is null) throw Invalid($"{field}.airportCodes", "Required.");
        if (group.AirportCodes.Any(code => code is null)) throw Invalid($"{field}.airportCodes", "Airport codes cannot be null.");
    }

    private static ItinerarySearchRequest Normalize(ItinerarySearchRequest request) => request switch
    {
        OrderedTripRequest ordered => ordered with
        {
            CabinClass = ordered.CabinClass.Trim().ToLowerInvariant(),
            Ranking = ordered.Ranking.Trim().ToLowerInvariant(),
            Legs = ordered.Legs.Select(leg => leg with { Id = leg.Id.Trim(), From = NormalizeGroup(leg.From), To = NormalizeGroup(leg.To) }).ToList()
        },
        OptimizedTripRequest optimized => optimized with
        {
            CabinClass = optimized.CabinClass.Trim().ToLowerInvariant(),
            Ranking = optimized.Ranking.Trim().ToLowerInvariant(),
            Start = NormalizeGroup(optimized.Start),
            FixedEnd = optimized.FixedEnd is null ? null : NormalizeGroup(optimized.FixedEnd),
            Destinations = optimized.Destinations.Select(destination => destination with { Group = NormalizeGroup(destination.Group) }).ToList()
        },
        _ => request
    };

    private static AirportGroupRequest NormalizeGroup(AirportGroupRequest group) => group with
    {
        Id = group.Id.Trim(),
        Label = group.Label.Trim(),
        AirportCodes = group.AirportCodes
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
    };
}
