using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Providers.FlightApi.Models;

namespace backend.Features.ItinerarySearch;

internal static class FlightApiOrderedFareMapper
{
    public static List<OrderedFareCandidate> Map(FlightApiOneWayResponse response, string requestedLegId)
    {
        var legs = response.Legs.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var segments = response.Segments.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var places = response.Places.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var carriers = response.Carriers.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First());
        var agents = response.Agents.Where(value => !string.IsNullOrWhiteSpace(value.Id)).GroupBy(value => value.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var mapped = new List<OrderedFareCandidate>();

        foreach (var itinerary in response.Itineraries)
        {
            var providerLegs = itinerary.LegIds.Where(legs.ContainsKey).Select(id => legs[id]).ToList();
            if (providerLegs.Count == 0) continue;
            var mappedSegments = providerLegs.SelectMany(leg => leg.SegmentIds)
                .Where(segments.ContainsKey)
                .Select(id => MapSegment(segments[id], places, carriers))
                .ToList();
            var first = providerLegs[0];
            var last = providerLegs[^1];
            var leg = new ItineraryLeg(
                requestedLegId,
                Place(first.OriginPlaceId, places),
                Place(last.DestinationPlaceId, places),
                DateTime.SpecifyKind(first.Departure, DateTimeKind.Unspecified),
                DateTime.SpecifyKind(last.Arrival, DateTimeKind.Unspecified),
                providerLegs.Sum(value => value.Duration),
                Math.Max(mappedSegments.Count - 1, 0),
                mappedSegments);

            foreach (var pricing in itinerary.PricingOptions)
            {
                if (pricing.Items.Count == 0)
                {
                    Add(mapped, itinerary.Id, pricing.Id, pricing.Price?.Amount, itinerary.DeepLink, "FlightApi", leg);
                    continue;
                }

                foreach (var item in pricing.Items)
                {
                    var agent = item.AgentId is not null && agents.TryGetValue(item.AgentId, out var found) ? found.Name : null;
                    Add(mapped, itinerary.Id, $"{pricing.Id}:{item.AgentId}", item.Price?.Amount ?? pricing.Price?.Amount, item.Url ?? itinerary.DeepLink, string.IsNullOrWhiteSpace(agent) ? "FlightApi" : $"FlightApi:{agent}", leg);
                }
            }
        }

        return mapped;
    }

    private static void Add(List<OrderedFareCandidate> target, string itineraryId, string priceId, decimal? price, string? link, string provider, ItineraryLeg leg)
    {
        var url = Link(link);
        var amount = price is > 0 ? price : TicketPrice(url);
        if (amount is not > 0 || string.IsNullOrWhiteSpace(url)) return;
        target.Add(new($"{itineraryId}:{priceId}", leg, amount.Value, "EUR", new BookingOption("Book this flight", url, amount.Value, "EUR", provider)));
    }

    private static ItinerarySegment MapSegment(FlightApiSegment segment, IReadOnlyDictionary<int, FlightApiPlace> places, IReadOnlyDictionary<int, FlightApiCarrier> carriers)
    {
        carriers.TryGetValue(segment.MarketingCarrierId ?? -1, out var carrier);
        return new(
            carrier?.Name ?? carrier?.DisplayCode ?? string.Empty,
            carrier?.DisplayCode ?? carrier?.Name ?? string.Empty,
            segment.MarketingFlightNumber ?? string.Empty,
            Place(segment.OriginPlaceId, places),
            Place(segment.DestinationPlaceId, places),
            DateTime.SpecifyKind(segment.Departure, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(segment.Arrival, DateTimeKind.Unspecified),
            segment.Duration);
    }

    private static string Place(int id, IReadOnlyDictionary<int, FlightApiPlace> places) => places.TryGetValue(id, out var place)
        ? First(place.Iata, place.DisplayCode, place.Name, id.ToString())
        : id.ToString();
    private static string First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static string Link(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return uri.ToString();
        return $"https://www.skyscanner.nl/{value.Trim().TrimStart('/')}";
    }
    private static decimal? TicketPrice(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "ticket_price", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(Uri.UnescapeDataString(parts[1]), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)) return amount;
        }
        return null;
    }
}

internal sealed record OrderedFareCandidate(string Id, ItineraryLeg Leg, decimal Price, string Currency, BookingOption Booking);
