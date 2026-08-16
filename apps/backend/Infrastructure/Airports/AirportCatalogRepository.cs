using backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace backend.Infrastructure.Airports;

public sealed class AirportCatalogRepository(AppDbContext dbContext) : IAirportCatalogRepository
{
    public async Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIataCodesAsync(
        IEnumerable<string> iataCodes,
        CancellationToken cancellationToken)
    {
        var codes = iataCodes
            .Select(code => code.Trim().ToUpperInvariant())
            .Where(code => code.Length == 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (codes.Length == 0) return new Dictionary<string, AirportCatalogEntry>(StringComparer.Ordinal);

        var airports = await dbContext.Airports
            .AsNoTracking()
            .Where(airport => codes.Contains(airport.Iata))
            .ToListAsync(cancellationToken);
        return airports.ToDictionary(airport => airport.Iata, StringComparer.Ordinal);
    }

    public Task<AirportCatalogMetadata?> GetMetadataAsync(CancellationToken cancellationToken) =>
        dbContext.AirportCatalogMetadata.AsNoTracking().SingleOrDefaultAsync(metadata => metadata.Id == AirportCatalogMetadata.SingletonId, cancellationToken);

    public async Task<bool> IsLiveCatalogIntactAsync(
        int expectedRowCount,
        IReadOnlyCollection<string> requiredIataCodes,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Airports.CountAsync(cancellationToken) != expectedRowCount) return false;

        var requiredCodes = requiredIataCodes
            .Select(code => code.Trim().ToUpperInvariant())
            .Where(code => code.Length == 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredCodes.Length == 0) return true;

        var presentRequiredCodeCount = await dbContext.Airports
            .CountAsync(airport => requiredCodes.Contains(airport.Iata), cancellationToken);
        return presentRequiredCodeCount == requiredCodes.Length;
    }

    public Task<int> DeleteAbandonedStagingAsync(CancellationToken cancellationToken) =>
        dbContext.AirportCatalogStaging.ExecuteDeleteAsync(cancellationToken);

    public Task<bool> HasStagingRowsAsync(CancellationToken cancellationToken) =>
        dbContext.AirportCatalogStaging.AnyAsync(cancellationToken);

    public async Task ReplaceAsync(
        IReadOnlyCollection<AirportCatalogEntry> airports,
        AirportCatalogImportSource source,
        DateTimeOffset importedAt,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var staging = airports.Select(airport => new AirportCatalogStagingEntry
        {
            BatchId = batchId,
            Iata = airport.Iata,
            Icao = airport.Icao,
            Name = airport.Name,
            City = airport.City,
            Subdivision = airport.Subdivision,
            CountryCode = airport.CountryCode,
            Latitude = airport.Latitude,
            Longitude = airport.Longitude,
            ElevationFeet = airport.ElevationFeet,
            Timezone = airport.Timezone,
            SourceUpdatedAt = importedAt
        }).ToArray();

        try
        {
            dbContext.AirportCatalogStaging.AddRange(staging);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();

            var stagedCount = await dbContext.AirportCatalogStaging.CountAsync(row => row.BatchId == batchId, cancellationToken);
            if (stagedCount != airports.Count)
                throw new InvalidOperationException($"Airport catalogue staging count mismatch: expected {airports.Count}, stored {stagedCount}.");

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.Airports.ExecuteDeleteAsync(cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO airports
                    ("Iata", "Icao", "Name", "City", "Subdivision", "CountryCode", "Latitude", "Longitude", "ElevationFeet", "Timezone", "SourceUpdatedAt")
                SELECT
                    "Iata", "Icao", "Name", "City", "Subdivision", "CountryCode", "Latitude", "Longitude", "ElevationFeet", "Timezone", "SourceUpdatedAt"
                FROM airport_catalog_staging
                WHERE "BatchId" = {{batchId}}
                """, cancellationToken);

            var metadata = await dbContext.AirportCatalogMetadata.SingleOrDefaultAsync(row => row.Id == AirportCatalogMetadata.SingletonId, cancellationToken);
            if (metadata is null)
            {
                metadata = new AirportCatalogMetadata
                {
                    SourceName = source.Name,
                    SourceUrl = source.Url,
                    SourceChecksum = source.Checksum,
                    LastAttemptedRefreshAt = importedAt
                };
                dbContext.AirportCatalogMetadata.Add(metadata);
            }
            metadata.SourceName = source.Name;
            metadata.SourceUrl = source.Url;
            metadata.SourceRevision = source.Revision;
            metadata.SourceChecksum = source.Checksum;
            metadata.LastSuccessfulImportAt = importedAt;
            metadata.ImportedRowCount = airports.Count;
            metadata.LastAttemptedRefreshAt = importedAt;
            metadata.LastFailureSummary = null;
            await dbContext.AirportCatalogStaging.Where(row => row.BatchId == batchId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            try
            {
                await dbContext.AirportCatalogStaging.Where(row => row.BatchId == batchId).ExecuteDeleteAsync(CancellationToken.None);
            }
            catch
            {
                // The next lock-owning refresh removes all abandoned staging batches before doing any work.
            }
            throw;
        }
    }

    public async Task RecordFailureAsync(
        AirportCatalogImportSource source,
        DateTimeOffset attemptedAt,
        string summary,
        CancellationToken cancellationToken)
    {
        var metadata = await dbContext.AirportCatalogMetadata.SingleOrDefaultAsync(row => row.Id == AirportCatalogMetadata.SingletonId, cancellationToken);
        if (metadata is null)
        {
            metadata = new AirportCatalogMetadata
            {
                SourceName = source.Name,
                SourceUrl = source.Url,
                SourceChecksum = source.Checksum,
                LastAttemptedRefreshAt = attemptedAt
            };
            dbContext.AirportCatalogMetadata.Add(metadata);
        }
        metadata.LastAttemptedRefreshAt = attemptedAt;
        metadata.LastFailureSummary = summary.Length <= 1024 ? summary : summary[..1024];
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordUnchangedAsync(
        AirportCatalogImportSource source,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken)
    {
        var metadata = await dbContext.AirportCatalogMetadata.SingleAsync(row => row.Id == AirportCatalogMetadata.SingletonId, cancellationToken);
        metadata.SourceName = source.Name;
        metadata.SourceUrl = source.Url;
        if (!string.IsNullOrWhiteSpace(source.Revision)) metadata.SourceRevision = source.Revision;
        metadata.SourceChecksum = source.Checksum;
        metadata.LastSuccessfulImportAt = attemptedAt;
        metadata.LastAttemptedRefreshAt = attemptedAt;
        metadata.LastFailureSummary = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
