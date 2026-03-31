namespace backend.Infrastructure.Models;

public record ProviderRoundTripSearchRequest(
    string OriginAirport,
    string DestinationAirport,
    DateOnly DepartureDate,
    DateOnly ReturnDate,
    int Adults,
    string CabinClass
);
