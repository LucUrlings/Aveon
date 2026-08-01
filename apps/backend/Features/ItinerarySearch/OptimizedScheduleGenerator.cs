using backend.Features.ItinerarySearch.Models;
using Microsoft.Extensions.Options;

namespace backend.Features.ItinerarySearch;

public sealed class OptimizedScheduleGenerator(IOptions<MultiDestinationSearchOptions> options) : IOptimizedScheduleGenerator
{
    private readonly int _scheduleLimit = options.Value.MaxActiveStates;

    public OptimizedSchedulePlan Generate(OptimizedTripRequest request)
    {
        var routeOrders = Permutations(request.Destinations).ToList();
        var minimumDays = routeOrders
            .Select(route => MinimumRemainingOffset(route, 0, request.EndpointMode))
            .Where(offset => offset < Impossible)
            .Select(offset => offset + 1)
            .DefaultIfEmpty(Impossible)
            .Min();
        var availableDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (minimumDays == Impossible || availableDays < minimumDays)
            throw new ItineraryValidationException("endDate", $"The selected stays and endpoint require at least {minimumDays} calendar days.");

        var schedules = new List<AbstractItinerarySchedule>();
        var evaluated = 0;
        var pruned = 0;
        var bounded = false;
        foreach (var route in routeOrders)
        {
            GenerateRoute(request, route, schedules, ref evaluated, ref pruned, ref bounded);
            if (bounded) break;
        }

        if (schedules.Count == 0)
            throw new ItineraryValidationException("endDate", "The selected date window cannot satisfy the exact and minimum stay rules.");

        var requiredLegs = request.Destinations.Count + (request.EndpointMode == "openEnded" ? 0 : 1);
        return new(
            new(requiredLegs, minimumDays, availableDays, routeOrders.Count, schedules.Count, bounded),
            schedules,
            evaluated,
            pruned);
    }

    public static DateOnly ArrivalDate(DateTime arrivalLocalTime) => DateOnly.FromDateTime(arrivalLocalTime);

    public static DateOnly StayDepartureDate(DateTime arrivalLocalTime, int nights) =>
        ArrivalDate(arrivalLocalTime).AddDays(nights);

    public static int StayNights(DateOnly arrivalDate, DateOnly departureDate) =>
        departureDate.DayNumber - arrivalDate.DayNumber;

    public static int StayNights(DateTime arrivalLocalTime, DateOnly departureDate) =>
        StayNights(ArrivalDate(arrivalLocalTime), departureDate);

    public static bool IsStaySatisfied(DateTime arrivalLocalTime, DateOnly departureDate, StayRuleRequest stay)
    {
        var actualNights = StayNights(arrivalLocalTime, departureDate);
        return stay.Mode == "exactNights" ? actualNights == stay.Nights : actualNights >= stay.Nights;
    }

    private void GenerateRoute(
        OptimizedTripRequest request,
        List<DestinationRequest> route,
        List<AbstractItinerarySchedule> schedules,
        ref int evaluated,
        ref int pruned,
        ref bool bounded)
    {
        var first = route[0];
        var legs = new List<AbstractScheduleLeg>
        {
            new("leg-1", request.Start.Id, first.Group.Id, request.StartDate)
        };
        BuildFromDestination(request, route, 0, request.StartDate, legs, [], schedules, ref evaluated, ref pruned, ref bounded);
    }

    private void BuildFromDestination(
        OptimizedTripRequest request,
        List<DestinationRequest> route,
        int index,
        DateOnly arrivalDate,
        List<AbstractScheduleLeg> legs,
        List<AbstractScheduleStay> stays,
        List<AbstractItinerarySchedule> schedules,
        ref int evaluated,
        ref int pruned,
        ref bool bounded)
    {
        if (bounded) return;
        evaluated++;
        var minimumOffset = MinimumRemainingOffset(route, index, request.EndpointMode);
        if (minimumOffset == Impossible || arrivalDate.AddDays(minimumOffset) > request.EndDate)
        {
            pruned++;
            return;
        }

        var destination = route[index];
        var isLast = index == route.Count - 1;
        if (isLast)
        {
            if (request.EndpointMode == "openEnded")
            {
                var abstractArrival = destination.Stay.Mode == "exactNights"
                    ? request.EndDate.AddDays(-destination.Stay.Nights)
                    : arrivalDate;
                var nights = StayNights(abstractArrival, request.EndDate);
                var stayValid = abstractArrival >= arrivalDate && (destination.Stay.Mode == "exactNights"
                    ? nights == destination.Stay.Nights
                    : nights >= destination.Stay.Nights);
                if (!stayValid)
                {
                    pruned++;
                    return;
                }
                var finalStays = new List<AbstractScheduleStay>(stays)
                {
                    new(destination.Group.Id, abstractArrival, request.EndDate, nights, destination.Stay.Mode, destination.Stay.Nights)
                };
                AddSchedule(route, legs, finalStays, schedules, ref bounded);
                return;
            }

            var terminal = request.EndpointMode == "returnToStart" ? request.Start : request.FixedEnd!;
            var generatedTerminal = false;
            foreach (var departureDate in DepartureDates(arrivalDate, destination.Stay, request.EndDate))
            {
                generatedTerminal = true;
                var finalLegs = new List<AbstractScheduleLeg>(legs)
                {
                    new($"leg-{legs.Count + 1}", destination.Group.Id, terminal.Id, departureDate, request.EndDate)
                };
                var abstractArrival = AbstractArrivalDate(arrivalDate, departureDate, destination.Stay);
                var nights = StayNights(abstractArrival, departureDate);
                var finalStays = new List<AbstractScheduleStay>(stays)
                {
                    new(destination.Group.Id, abstractArrival, departureDate, nights, destination.Stay.Mode, destination.Stay.Nights)
                };
                AddSchedule(route, finalLegs, finalStays, schedules, ref bounded);
                if (bounded) return;
            }
            if (!generatedTerminal) pruned++;
            return;
        }

        var nextMinimum = MinimumRemainingOffset(route, index + 1, request.EndpointMode);
        var latestDeparture = request.EndDate.AddDays(-nextMinimum);
        foreach (var departureDate in DepartureDates(arrivalDate, destination.Stay, latestDeparture))
        {
            var next = route[index + 1];
            var nextLegs = new List<AbstractScheduleLeg>(legs)
            {
                new($"leg-{legs.Count + 1}", destination.Group.Id, next.Group.Id, departureDate)
            };
            var abstractArrival = AbstractArrivalDate(arrivalDate, departureDate, destination.Stay);
            var nights = StayNights(abstractArrival, departureDate);
            var nextStays = new List<AbstractScheduleStay>(stays)
            {
                new(destination.Group.Id, abstractArrival, departureDate, nights, destination.Stay.Mode, destination.Stay.Nights)
            };
            BuildFromDestination(request, route, index + 1, departureDate, nextLegs, nextStays, schedules, ref evaluated, ref pruned, ref bounded);
            if (bounded) return;
        }
    }

    private void AddSchedule(
        List<DestinationRequest> route,
        List<AbstractScheduleLeg> legs,
        List<AbstractScheduleStay> stays,
        List<AbstractItinerarySchedule> schedules,
        ref bool bounded)
    {
        var id = string.Join("_", route.Select(destination => destination.Group.Id)) + "-" +
                 string.Join("-", legs.Select(leg => leg.DepartureDate.ToString("yyyyMMdd")));
        schedules.Add(new(id, route.Select(destination => destination.Group.Id).ToList(), legs, stays));
        if (schedules.Count >= _scheduleLimit) bounded = true;
    }

    private static IEnumerable<DateOnly> DepartureDates(DateOnly arrivalDate, StayRuleRequest stay, DateOnly latestDate)
    {
        var requested = arrivalDate.AddDays(stay.Nights);
        if (stay.Mode == "exactNights")
        {
            var exactEarliest = requested > arrivalDate ? requested : arrivalDate.AddDays(1);
            for (var date = exactEarliest; date <= latestDate; date = date.AddDays(1)) yield return date;
            yield break;
        }

        var earliest = requested > arrivalDate ? requested : arrivalDate.AddDays(1);
        for (var date = earliest; date <= latestDate; date = date.AddDays(1)) yield return date;
    }

    private static DateOnly AbstractArrivalDate(DateOnly earliestArrivalDate, DateOnly departureDate, StayRuleRequest stay) =>
        stay.Mode == "exactNights" ? departureDate.AddDays(-stay.Nights) : earliestArrivalDate;

    private const int Impossible = int.MaxValue / 4;

    private static int MinimumRemainingOffset(List<DestinationRequest> route, int index, string endpointMode)
    {
        var total = 0;
        for (var current = index; current < route.Count; current++)
        {
            var isFinal = current == route.Count - 1;
            var stay = route[current].Stay;
            if (isFinal && endpointMode == "openEnded")
            {
                total += stay.Nights;
                continue;
            }

            total += Math.Max(1, stay.Nights);
        }
        return total;
    }

    private static IEnumerable<List<DestinationRequest>> Permutations(List<DestinationRequest> items)
    {
        if (items.Count == 1)
        {
            yield return [items[0]];
            yield break;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var remaining = items.Where((_, candidate) => candidate != index).ToList();
            foreach (var suffix in Permutations(remaining))
                yield return [items[index], .. suffix];
        }
    }
}
