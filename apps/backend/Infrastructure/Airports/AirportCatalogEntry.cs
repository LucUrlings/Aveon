namespace backend.Infrastructure.Airports;

public sealed class AirportCatalogEntry
{
    public required string Iata { get; set; }
    public string? Icao { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public string? Subdivision { get; set; }
    public required string CountryCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? ElevationFeet { get; set; }
    public string? Timezone { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
}

public sealed class AirportCatalogStagingEntry
{
    public Guid BatchId { get; set; }
    public required string Iata { get; set; }
    public string? Icao { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public string? Subdivision { get; set; }
    public required string CountryCode { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int? ElevationFeet { get; set; }
    public string? Timezone { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
}

public sealed class AirportCatalogMetadata
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public required string SourceName { get; set; }
    public required string SourceUrl { get; set; }
    public string? SourceRevision { get; set; }
    public required string SourceChecksum { get; set; }
    public DateTimeOffset? LastSuccessfulImportAt { get; set; }
    public int ImportedRowCount { get; set; }
    public DateTimeOffset LastAttemptedRefreshAt { get; set; }
    public string? LastFailureSummary { get; set; }
}
