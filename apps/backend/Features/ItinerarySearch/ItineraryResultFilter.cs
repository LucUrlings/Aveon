using backend.Features.ItinerarySearch.Models;

namespace backend.Features.ItinerarySearch;

internal static class ItineraryResultFilter
{
    public static ItinerarySearchSessionResponse Apply(ItinerarySearchSessionResponse session, ItineraryResultsQuery? query)
    {
        query ??= new();
        var canonical = session.Results;
        var airlines = Parse(query.Airlines);
        var sources = Parse(query.BookingSources);
        var departures = Parse(query.DepartureAirports);
        var arrivals = Parse(query.ArrivalAirports);
        var departureTime = ParseRange(query.DepartureTime);
        var arrivalTime = ParseRange(query.ArrivalTime);

        var filtered = canonical
            .Where(result => MatchesStops(result, query))
            .Where(result => airlines.Count == 0 || result.Legs.All(leg => (leg.Segments ?? []).All(segment => airlines.Contains(segment.MarketingCarrierCode) || airlines.Contains(segment.MarketingCarrierName))))
            .Where(result => sources.Count == 0 || result.BookingOptions.All(option => sources.Contains(option.Provider)))
            .Where(result => departures.Count == 0 || result.Legs.FirstOrDefault() is { } first && departures.Contains(first.OriginAirport))
            .Where(result => arrivals.Count == 0 || result.Legs.LastOrDefault() is { } last && arrivals.Contains(last.DestinationAirport))
            .Where(result => !query.MaxPrice.HasValue || result.TotalPrice <= query.MaxPrice)
            .Where(result => !query.MaxDurationMinutes.HasValue || result.TotalFlightDurationMinutes <= query.MaxDurationMinutes)
            .Where(result => !query.MaxBookingCount.HasValue || result.BookingCount <= query.MaxBookingCount)
            .Where(result => !query.AllowAirportSwitches.HasValue || query.AllowAirportSwitches.Value || result.AirportSwitches == 0)
            .Where(result => string.IsNullOrWhiteSpace(query.BookingType) || string.Equals(result.BookingType, query.BookingType, StringComparison.OrdinalIgnoreCase))
            .Where(result => departureTime is null || result.Legs.FirstOrDefault() is { } first && InRange(first.DepartureLocalTime, departureTime.Value))
            .Where(result => arrivalTime is null || result.Legs.LastOrDefault() is { } last && InRange(last.ArrivalLocalTime, arrivalTime.Value))
            .ToList();

        filtered = (query.Ranking?.Trim().ToLowerInvariant()) switch
        {
            "cheapest" => filtered.OrderBy(result => result.TotalPrice).ThenBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.Id, StringComparer.Ordinal).ToList(),
            "fastest" => filtered.OrderBy(result => result.TotalFlightDurationMinutes).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal).ToList(),
            _ => filtered.OrderBy(result => result.RankingBreakdown.Score).ThenBy(result => result.TotalPrice).ThenBy(result => result.Id, StringComparer.Ordinal).ToList()
        };

        var page = Math.Max(query.Page ?? 1, 1);
        var pageSize = Math.Clamp(query.PageSize ?? 25, 1, 100);
        var total = filtered.Count;
        var results = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return session with
        {
            Results = results,
            Pagination = new(page, pageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)),
            Filters = BuildMetadata(canonical)
        };
    }

    private static bool MatchesStops(ItineraryResult result, ItineraryResultsQuery query)
    {
        if (!query.Direct.HasValue && !query.OneStop.HasValue && !query.TwoPlusStops.HasValue) return true;
        return result.Legs.All(leg => leg.Stops switch
        {
            0 => query.Direct == true,
            1 => query.OneStop == true,
            _ => query.TwoPlusStops == true
        });
    }

    private static ItineraryFilterMetadata BuildMetadata(List<ItineraryResult> results) => new(
        Count(results.SelectMany(result => result.Legs).SelectMany(leg => leg.Segments ?? []).Select(segment => (segment.MarketingCarrierCode, segment.MarketingCarrierName))),
        Count(results.SelectMany(result => result.BookingOptions).Select(option => (option.Provider, option.Provider))),
        Count(results.Where(result => result.Legs.Count > 0).Select(result => (result.Legs[0].OriginAirport, result.Legs[0].OriginAirport))),
        Count(results.Where(result => result.Legs.Count > 0).Select(result => (result.Legs[^1].DestinationAirport, result.Legs[^1].DestinationAirport))),
        results.Count == 0 ? null : results.Min(result => result.TotalPrice),
        results.Count == 0 ? null : results.Max(result => result.TotalPrice),
        results.Count == 0 ? null : results.Max(result => result.TotalFlightDurationMinutes),
        results.Count == 0 ? null : results.Max(result => result.BookingCount),
        results.Count == 0 ? null : results.Max(result => result.AirportSwitches));

    private static List<ItineraryFilterOption> Count(IEnumerable<(string Value, string Label)> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value.Value))
        .GroupBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
        .Select(group => new ItineraryFilterOption(group.Key, group.First().Label, group.Count()))
        .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static HashSet<string> Parse(string? raw) => new((raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
    private static (int Min, int Max)? ParseRange(string? raw)
    {
        var values = (raw ?? string.Empty).Split('-', 2);
        return values.Length == 2 && int.TryParse(values[0], out var min) && int.TryParse(values[1], out var max) ? (Math.Min(min, max), Math.Max(min, max)) : null;
    }
    private static bool InRange(DateTime value, (int Min, int Max) range)
    {
        var minutes = value.Hour * 60 + value.Minute;
        return minutes >= range.Min && minutes <= range.Max;
    }
}
