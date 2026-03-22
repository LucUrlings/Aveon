namespace backend.Infrastructure.Models;

public record ProviderSearchRequest(
    string OriginAirport,
    string DestinationAirport,
    DateOnly DepartureDate,
    int Adults,
    string CabinClass
);
