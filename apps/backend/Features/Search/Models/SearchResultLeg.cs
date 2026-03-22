namespace backend.Features.Search.Models;

public record SearchResultLeg(
    string OriginAirport,
    string DestinationAirport,
    DateTime DepartureUtc,
    DateTime ArrivalUtc,
    int DurationMinutes,
    List<SearchResultSegment> Segments
);
