namespace backend.Features.Search.Models;

public record SearchResultLeg(
    string OriginAirport,
    string DestinationAirport,
    DateTime DepartureLocalTime,
    DateTime ArrivalLocalTime,
    int DurationMinutes,
    List<SearchResultSegment> Segments
);
