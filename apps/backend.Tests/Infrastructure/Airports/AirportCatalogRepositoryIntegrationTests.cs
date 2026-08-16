using backend.Infrastructure.Airports;
using backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Data.Common;
using Xunit;

namespace backend.Tests.Infrastructure.Airports;

public sealed class AirportCatalogRepositoryIntegrationTests
{
    [Fact]
    public async Task PostgresRefreshLock_AllowsOnlyOneImporterAndCanBeReacquiredAfterRelease()
    {
        var connectionString = Environment.GetEnvironmentVariable("AVEON_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString
        }).Build();
        var firstLock = new PostgresAirportCatalogRefreshLock(configuration);
        var secondLock = new PostgresAirportCatalogRefreshLock(configuration);

        var firstLease = await firstLock.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(firstLease);
        Assert.Null(await secondLock.TryAcquireAsync(CancellationToken.None));
        await firstLease.DisposeAsync();

        var secondLease = await secondLock.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(secondLease);
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task ReplaceAsync_IsAtomicAndMaintainsStagingAndMetadataIntegrity()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable("AVEON_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(serverConnectionString)) return;

        var databaseName = $"aveon_catalog_{Guid.NewGuid():N}";
        var serverBuilder = new NpgsqlConnectionStringBuilder(serverConnectionString);
        await using (var server = new NpgsqlConnection(serverBuilder.ConnectionString))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", server);
            await create.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName };
        try
        {
            var commitInterceptor = new FailNextCommitInterceptor();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(databaseBuilder.ConnectionString)
                .AddInterceptors(commitInterceptor)
                .Options;
            await using var db = new AppDbContext(options);
            await db.Database.MigrateAsync();
            var repository = new AirportCatalogRepository(db);
            var importedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
            var source = new AirportCatalogImportSource("test", "https://example.test/airports.csv", "revision-1", new string('a', 64));

            await repository.ReplaceAsync([Airport("DUB", importedAt)], source, importedAt, CancellationToken.None);
            Assert.Equal("DUB", Assert.Single(await db.Airports.AsNoTracking().ToListAsync()).Iata);
            Assert.True(await repository.IsLiveCatalogIntactAsync(1, ["DUB"], CancellationToken.None));
            Assert.False(await repository.IsLiveCatalogIntactAsync(2, ["DUB"], CancellationToken.None));
            Assert.False(await repository.IsLiveCatalogIntactAsync(1, ["AMS"], CancellationToken.None));

            var unchangedSource = source with { Revision = "revision-unchanged" };
            await repository.RecordUnchangedAsync(unchangedSource, importedAt.AddHours(1), CancellationToken.None);
            var unchangedMetadata = await db.AirportCatalogMetadata.AsNoTracking().SingleAsync();
            Assert.Equal("revision-unchanged", unchangedMetadata.SourceRevision);
            Assert.Equal(importedAt.AddHours(1), unchangedMetadata.LastSuccessfulImportAt);
            await repository.RecordUnchangedAsync(unchangedSource with { Revision = null }, importedAt.AddHours(2), CancellationToken.None);
            unchangedMetadata = await db.AirportCatalogMetadata.AsNoTracking().SingleAsync();
            Assert.Equal("revision-unchanged", unchangedMetadata.SourceRevision);
            Assert.Equal(importedAt.AddHours(2), unchangedMetadata.LastSuccessfulImportAt);

            db.AirportCatalogStaging.Add(new AirportCatalogStagingEntry
            {
                BatchId = Guid.NewGuid(),
                Iata = "OLD",
                Name = "Abandoned staging row",
                City = "Old",
                CountryCode = "IE",
                Latitude = 1,
                Longitude = 1,
                SourceUpdatedAt = importedAt.AddDays(-1)
            });
            await db.SaveChangesAsync();
            Assert.True(await repository.HasStagingRowsAsync(CancellationToken.None));
            Assert.Equal(1, await repository.DeleteAbandonedStagingAsync(CancellationToken.None));
            Assert.False(await repository.HasStagingRowsAsync(CancellationToken.None));
            Assert.Empty(await db.AirportCatalogStaging.AsNoTracking().ToListAsync());

            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE airports
                ADD CONSTRAINT "CK_airports_reject_transaction_test_row"
                CHECK ("Iata" <> 'BAD')
                """);
            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceAsync(
                [Airport("BAD", importedAt)],
                source with { Revision = "must-rollback", Checksum = new string('c', 64) },
                importedAt.AddHours(3),
                CancellationToken.None));
            Assert.Equal("DUB", Assert.Single(await db.Airports.AsNoTracking().ToListAsync()).Iata);
            Assert.Empty(await db.AirportCatalogStaging.AsNoTracking().ToListAsync());
            var rolledBackMetadata = await db.AirportCatalogMetadata.AsNoTracking().SingleAsync();
            Assert.Equal("revision-unchanged", rolledBackMetadata.SourceRevision);
            Assert.Equal(1, rolledBackMetadata.ImportedRowCount);

            commitInterceptor.FailNextCommit = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReplaceAsync(
                [Airport("AMS", importedAt)],
                source with { Revision = "commit-must-rollback", Checksum = new string('d', 64) },
                importedAt.AddHours(4),
                CancellationToken.None));
            Assert.Equal("DUB", Assert.Single(await db.Airports.AsNoTracking().ToListAsync()).Iata);
            Assert.Empty(await db.AirportCatalogStaging.AsNoTracking().ToListAsync());
            rolledBackMetadata = await db.AirportCatalogMetadata.AsNoTracking().SingleAsync();
            Assert.Equal("revision-unchanged", rolledBackMetadata.SourceRevision);
            Assert.Equal(new string('a', 64), rolledBackMetadata.SourceChecksum);
            Assert.Equal(1, rolledBackMetadata.ImportedRowCount);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                repository.ReplaceAsync([Airport("AMS", importedAt)], source, importedAt, new CancellationToken(canceled: true)));
            Assert.Equal("DUB", Assert.Single(await db.Airports.AsNoTracking().ToListAsync()).Iata);

            await Assert.ThrowsAnyAsync<Exception>(() => repository.ReplaceAsync(
                [Airport("AMS", importedAt), Airport("AMS", importedAt)], source, importedAt, CancellationToken.None));
            Assert.Equal("DUB", Assert.Single(await db.Airports.AsNoTracking().ToListAsync()).Iata);

            await repository.ReplaceAsync([Airport("AMS", importedAt), Airport("JFK", importedAt)], source with { Revision = "revision-2", Checksum = new string('b', 64) }, importedAt.AddDays(1), CancellationToken.None);
            Assert.Equal(["AMS", "JFK"], await db.Airports.AsNoTracking().OrderBy(airport => airport.Iata).Select(airport => airport.Iata).ToListAsync());
            Assert.Empty(await db.AirportCatalogStaging.AsNoTracking().ToListAsync());
            var metadata = await db.AirportCatalogMetadata.AsNoTracking().SingleAsync();
            Assert.Equal(2, metadata.ImportedRowCount);
            Assert.Equal("revision-2", metadata.SourceRevision);
            Assert.Equal(new string('b', 64), metadata.SourceChecksum);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(serverBuilder.ConnectionString);
            await server.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static AirportCatalogEntry Airport(string iata, DateTimeOffset importedAt) => new()
    {
        Iata = iata,
        Name = iata,
        City = iata,
        CountryCode = "IE",
        Latitude = 1,
        Longitude = 1,
        SourceUpdatedAt = importedAt
    };

    private sealed class FailNextCommitInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!FailNextCommit) return ValueTask.FromResult(result);
            FailNextCommit = false;
            throw new InvalidOperationException("Injected failure after catalogue and metadata writes, before transaction commit.");
        }
    }
}
