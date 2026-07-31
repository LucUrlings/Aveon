namespace backend.Features.Search.Models;

public record SearchRequest(
    List<string> OriginAirports,
    List<string> DestinationAirports,
    List<DateOnly> DepartureDates,
    List<DateOnly> ReturnDates,
    int Adults,
    string CabinClass)
{
    public IEnumerable<DateOnly> GetDepartureDates()
    {
        return (DepartureDates ?? [])
            .Distinct()
            .OrderBy(date => date);
    }

    public IEnumerable<DateOnly> GetReturnDates()
        => (ReturnDates ?? [])
            .Distinct()
            .OrderBy(date => date);
}
