namespace backend.Infrastructure.Airports;

public sealed record AirportCatalogOptions
{
    public const string SectionName = "AirportCatalog";

    public bool RefreshEnabled { get; init; } = true;
    public string SourceName { get; init; } = "mborsetti/airportsdata";
    public string SourceUrl { get; init; } = "https://raw.githubusercontent.com/mborsetti/airportsdata/main/airportsdata/airports.csv";
    public string RevisionApiUrl { get; init; } = "https://api.github.com/repos/mborsetti/airportsdata/commits?path=airportsdata%2Fairports.csv&per_page=1";
    public string RevisionDownloadUrlTemplate { get; init; } = "https://raw.githubusercontent.com/mborsetti/airportsdata/{revision}/airportsdata/airports.csv";
    public int RefreshAgeDays { get; init; } = 30;
    public int CheckIntervalHours { get; init; } = 24;
    public int DownloadTimeoutSeconds { get; init; } = 60;
    public int MaximumDownloadBytes { get; init; } = 10 * 1024 * 1024;
    public int MinimumAirportCount { get; init; } = 5_000;
    public int MaximumRowDropPercent { get; init; } = 10;
    public string[] RequiredIataCodes { get; init; } = ["DUB", "AMS", "LHR", "CDG", "FRA", "JFK", "ATL", "DXB", "DOH", "SIN", "HND", "SYD"];
}
