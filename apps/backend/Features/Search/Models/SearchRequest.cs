namespace backend.Features.Search.Models;

public record SearchRequest(
    List<string> OriginAirports,
    List<string> DestinationAirports,
    DateOnly DepartDateFrom,
    DateOnly DepartDateTo,
    DateOnly? ReturnDateFrom,
    DateOnly? ReturnDateTo,
    int Adults,
    string CabinClass)
{
    public IEnumerable<DateOnly> GetDepartureDates()
    {
        for (var date = DepartDateFrom; date <= DepartDateTo; date = date.AddDays(1))
        {
            yield return date;
        }
    }
}
