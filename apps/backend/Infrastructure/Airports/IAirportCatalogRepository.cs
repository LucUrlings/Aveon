namespace backend.Infrastructure.Airports;

public interface IAirportCatalogRepository
{
    Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIataCodesAsync(IEnumerable<string> iataCodes, CancellationToken cancellationToken);
    Task<AirportCatalogMetadata?> GetMetadataAsync(CancellationToken cancellationToken);
    Task<bool> IsLiveCatalogIntactAsync(int expectedRowCount, IReadOnlyCollection<string> requiredIataCodes, CancellationToken cancellationToken);
    Task<bool> HasStagingRowsAsync(CancellationToken cancellationToken);
    Task<int> DeleteAbandonedStagingAsync(CancellationToken cancellationToken);
    Task ReplaceAsync(IReadOnlyCollection<AirportCatalogEntry> airports, AirportCatalogImportSource source, DateTimeOffset importedAt, CancellationToken cancellationToken);
    Task RecordUnchangedAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, CancellationToken cancellationToken);
    Task RecordFailureAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, string summary, CancellationToken cancellationToken);
}

public sealed record AirportCatalogImportSource(string Name, string Url, string? Revision, string Checksum);
