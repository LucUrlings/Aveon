using backend.Infrastructure.Models;

namespace backend.Infrastructure.Caching;

public static class ProviderCacheKeyBuilder
{
    public static string BuildFlightApiOneWaySearchKey(ProviderSearchRequest request)
    {
        var origin = request.OriginAirport.Trim().ToUpperInvariant();
        var destination = request.DestinationAirport.Trim().ToUpperInvariant();
        var cabinClass = request.CabinClass.Trim().ToLowerInvariant();

        return string.Join(
            ":",
            "provider",
            "flightapi",
            "oneway",
            origin,
            destination,
            request.DepartureDate.ToString("yyyy-MM-dd"),
            request.Adults,
            cabinClass);
    }

    public static string BuildFlightApiRoundTripSearchKey(ProviderRoundTripSearchRequest request)
    {
        var origin = request.OriginAirport.Trim().ToUpperInvariant();
        var destination = request.DestinationAirport.Trim().ToUpperInvariant();
        var cabinClass = request.CabinClass.Trim().ToLowerInvariant();

        return string.Join(
            ":",
            "provider",
            "flightapi",
            "roundtrip",
            origin,
            destination,
            request.DepartureDate.ToString("yyyy-MM-dd"),
            request.ReturnDate.ToString("yyyy-MM-dd"),
            request.Adults,
            cabinClass);
    }

    public static string BuildFlightApiAirportLookupKey(string query) =>
        string.Join(
            ":",
            "provider",
            "flightapi",
            "airport-lookup",
            query.Trim().ToLowerInvariant());
}
