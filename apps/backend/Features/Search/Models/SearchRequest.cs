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

    public IEnumerable<DateOnly> GetReturnDates()
    {
        if (ReturnDateFrom is null || ReturnDateTo is null)
        {
            return [];
        }

        var start = ReturnDateFrom.Value <= ReturnDateTo.Value ? ReturnDateFrom.Value : ReturnDateTo.Value;
        var end = ReturnDateFrom.Value <= ReturnDateTo.Value ? ReturnDateTo.Value : ReturnDateFrom.Value;
        var dates = new List<DateOnly>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            dates.Add(date);
        }

        return dates;
    }
}
