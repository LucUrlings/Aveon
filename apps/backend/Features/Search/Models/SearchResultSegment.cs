namespace backend.Features.Search.Models;

public record SearchResultSegment(
    string MarketingCarrierName,
    string MarketingCarrierCode,
    string FlightNumber,
    string OriginAirport,
    string DestinationAirport,
    DateTime DepartureUtc,
    DateTime ArrivalUtc,
    int DurationMinutes
);
