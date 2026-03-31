namespace backend.Features.Search.Models;

public record SearchResultsQuery
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 200;

    public bool? Direct { get; init; }

    public bool? OneStop { get; init; }

    public bool? TwoPlusStop { get; init; }

    public string? Providers { get; init; }

    public string? Airlines { get; init; }

    public string? DepartureAirports { get; init; }

    public string? ArrivalAirports { get; init; }

    public int? MaxDuration { get; init; }

    public string? DepartureTime { get; init; }

    public string? ArrivalTime { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public bool HasStopFilter => Direct.HasValue || OneStop.HasValue || TwoPlusStop.HasValue;

    public bool IsPaginationRequested => Page.HasValue || PageSize.HasValue;

    public int GetPage() => Math.Max(Page ?? 1, 1);

    public int GetPageSize() => Math.Clamp(PageSize ?? DefaultPageSize, 1, MaxPageSize);

    public List<string> GetProviders() => ParseStringList(Providers);

    public List<string> GetAirlines() => ParseStringList(Airlines);

    public List<string> GetDepartureAirports() => ParseStringList(DepartureAirports)
        .Select(value => value.ToUpperInvariant())
        .ToList();

    public List<string> GetArrivalAirports() => ParseStringList(ArrivalAirports)
        .Select(value => value.ToUpperInvariant())
        .ToList();

    public (int Min, int Max)? GetDepartureTimeRange() => ParseIntRange(DepartureTime);

    public (int Min, int Max)? GetArrivalTimeRange() => ParseIntRange(ArrivalTime);

    private static List<string> ParseStringList(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static (int Min, int Max)? ParseIntRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var min) ||
            !int.TryParse(parts[1], out var max))
        {
            return null;
        }

        return (Math.Min(min, max), Math.Max(min, max));
    }
}
