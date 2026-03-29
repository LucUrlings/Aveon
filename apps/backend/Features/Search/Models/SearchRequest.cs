namespace backend.Features.Search.Models;

public record SearchRequest(
    List<string> OriginAirports,
    List<string> DestinationAirports,
    List<DateOnly> SelectedDates,
    DateOnly? ReturnDateFrom,
    DateOnly? ReturnDateTo,
    int Adults,
    string CabinClass)
{
    public IEnumerable<DateOnly> GetDepartureDates()
    {
        return SelectedDates
            .Distinct()
            .OrderBy(date => date);
    }
}
