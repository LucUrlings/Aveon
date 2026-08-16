using backend.Infrastructure.Airports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace backend.Tests.Infrastructure.Airports;

public sealed class AirportCatalogRefreshServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly string RevisionA = new('a', 40);
    private static readonly string RevisionB = new('b', 40);
    private const string ValidCsv = "icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid\nEIDW,DUB,Dublin Airport,Dublin,,IE,242,53.4213,-6.2701,Europe/Dublin,\n";

    [Fact]
    public async Task RecentCatalogue_SkipsLockAndDownload()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-1), "old") };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.NotDue, result.Status);
        Assert.Equal(0, refreshLock.AcquireCount);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task RecentCatalogueWithoutGitRevision_BootstrapsRevisionTracking()
    {
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidCsv))).ToLowerInvariant();
        var metadata = Metadata(Now.AddDays(-1), checksum);
        metadata.SourceRevision = null;
        var repository = new FixtureRepository { Metadata = metadata };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, RevisionB);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Unchanged, result.Status);
        Assert.Null(repository.Replacement);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Equal(RevisionB, repository.UnchangedSource?.Revision);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Fact]
    public async Task ForcedRefresh_UsesTheGuardedImportPathEvenWhenCatalogueIsRecent()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-1), "old") };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: true, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Refreshed, result.Status);
        Assert.Equal(1, refreshLock.AcquireCount);
        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Equal(1, repository.DeleteAbandonedStagingCallCount);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Equal($"https://example.test/raw/{RevisionB}/airports.csv", handler.LastDownloadUrl);
        Assert.Equal("DUB", Assert.Single(repository.Replacement!).Iata);
    }

    [Fact]
    public async Task ForcedRefresh_ReplacesCatalogueWhenRevisionAndChecksumAreUnchanged()
    {
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidCsv))).ToLowerInvariant();
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-1), checksum, RevisionA) };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, RevisionA);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: true, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Refreshed, result.Status);
        Assert.Equal("DUB", Assert.Single(repository.Replacement!).Iata);
        Assert.Equal(RevisionA, repository.ReplacementSource?.Revision);
        Assert.Equal(checksum, repository.ReplacementSource?.Checksum);
        Assert.Null(repository.UnchangedAt);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Equal($"https://example.test/raw/{RevisionA}/airports.csv", handler.LastDownloadUrl);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Fact]
    public async Task RecentCatalogueWithAbandonedStaging_AcquiresLockAndCleansWithoutDownloading()
    {
        var repository = new FixtureRepository
        {
            Metadata = Metadata(Now.AddDays(-1), "old"),
            HasStagingRows = true,
            AbandonedStagingRowsToDelete = 7
        };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.NotDue, result.Status);
        Assert.Equal(1, refreshLock.AcquireCount);
        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Equal(1, repository.DeleteAbandonedStagingCallCount);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DueCatalogue_DownloadsValidatesReplacesAndReleasesLock()
    {
        var repository = new FixtureRepository();
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Refreshed, result.Status);
        Assert.Equal("DUB", Assert.Single(repository.Replacement!).Iata);
        Assert.Equal(1, repository.DeleteAbandonedStagingCallCount);
        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Null(repository.FailureSummary);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Equal(RevisionB, repository.ReplacementSource?.Revision);
        Assert.Equal($"https://example.test/raw/{RevisionB}/airports.csv", repository.ReplacementSource?.Url);
    }

    [Fact]
    public async Task CurrentMetadataWithDamagedLiveCatalogue_ForcesReplacementEvenWhenChecksumIsUnchanged()
    {
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidCsv))).ToLowerInvariant();
        var repository = new FixtureRepository
        {
            Metadata = Metadata(Now.AddDays(-1), checksum),
            LiveCatalogIntact = false
        };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, RevisionA);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Refreshed, result.Status);
        Assert.Equal("DUB", Assert.Single(repository.Replacement!).Iata);
        Assert.Null(repository.UnchangedAt);
        Assert.Equal(1, repository.DeleteAbandonedStagingCallCount);
        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Equal($"https://example.test/raw/{RevisionA}/airports.csv", handler.LastDownloadUrl);
    }

    [Fact]
    public async Task UnchangedRevision_SkipsCsvDownloadAndRecordsSuccessfulCheck()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), new string('c', 64), RevisionA) };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, RevisionA);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Unchanged, result.Status);
        Assert.Null(repository.Replacement);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(0, handler.DownloadCallCount);
        Assert.Equal(RevisionA, repository.UnchangedSource?.Revision);
        Assert.Equal($"https://example.test/raw/{RevisionA}/airports.csv", repository.UnchangedSource?.Url);
        Assert.Equal(new string('c', 64), repository.UnchangedSource?.Checksum);
    }

    [Fact]
    public async Task ChangedRevisionWithUnchangedContent_RecordsNewRevisionWithoutReplacingCatalogue()
    {
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidCsv))).ToLowerInvariant();
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), checksum, RevisionA) };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, RevisionB);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.Unchanged, result.Status);
        Assert.Null(repository.Replacement);
        Assert.Equal(Now, repository.UnchangedAt);
        Assert.Equal(RevisionB, repository.UnchangedSource?.Revision);
        Assert.Equal(checksum, repository.UnchangedSource?.Checksum);
        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
    }

    [Fact]
    public async Task FailedImport_RecordsFailureKeepsPreviousCatalogueAndReleasesLock()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var service = CreateService(repository, refreshLock, new FixtureHandler("not,a,catalogue\n"));

        await Assert.ThrowsAsync<AirportCatalogImportException>(() => service.RefreshAsync(force: false, CancellationToken.None));

        Assert.Null(repository.Replacement);
        Assert.Contains("AirportCatalogImportException", repository.FailureSummary);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-commit-sha")]
    public async Task MissingOrInvalidRevision_RecordsFailureAndDoesNotDownloadCsv(string revision)
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(ValidCsv, revision);
        var service = CreateService(repository, refreshLock, handler);

        var exception = await Assert.ThrowsAsync<AirportCatalogImportException>(() => service.RefreshAsync(force: false, CancellationToken.None));

        Assert.Contains("valid Git commit SHA", exception.Message);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(0, handler.DownloadCallCount);
        Assert.Null(repository.Replacement);
        Assert.Contains("AirportCatalogImportException", repository.FailureSummary);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Fact]
    public async Task RevisionLookupFailure_RecordsFailureAndKeepsPreviousCatalogue()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var handler = new FixtureHandler(new HttpRequestException("GitHub unavailable"));
        var service = CreateService(repository, refreshLock, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync(force: false, CancellationToken.None));

        Assert.Null(repository.Replacement);
        Assert.Contains("HttpRequestException", repository.FailureSummary);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Fact]
    public async Task ContendedLock_DoesNotDownloadOrReplace()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock(acquired: false);
        var handler = new FixtureHandler(ValidCsv);
        var service = CreateService(repository, refreshLock, handler);

        var result = await service.RefreshAsync(force: false, CancellationToken.None);

        Assert.Equal(AirportCatalogRefreshStatus.AlreadyRunning, result.Status);
        Assert.Equal(0, handler.CallCount);
        Assert.Null(repository.Replacement);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndReleasesLockWithoutRecordingFailure()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var service = CreateService(repository, refreshLock, new WaitingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(force: false, cancellation.Token));

        Assert.Equal(1, refreshLock.DisposeCount);
        Assert.Null(repository.FailureSummary);
    }

    [Fact]
    public async Task RevisionLookupTimeout_RecordsFailureAndReleasesLock()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var service = CreateService(repository, refreshLock, new WaitingHandler(), downloadTimeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => service.RefreshAsync(force: false, CancellationToken.None));

        Assert.Contains("timed out after 1 seconds", exception.Message);
        Assert.Contains("TimeoutException", repository.FailureSummary);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    [Fact]
    public async Task CsvDownloadTimeout_RecordsFailureAndKeepsPreviousCatalogue()
    {
        var repository = new FixtureRepository { Metadata = Metadata(Now.AddDays(-31), "old") };
        var refreshLock = new FixtureLock();
        var handler = new WaitingDownloadHandler();
        var service = CreateService(repository, refreshLock, handler, downloadTimeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => service.RefreshAsync(force: false, CancellationToken.None));

        Assert.Contains("download timed out after 1 seconds", exception.Message);
        Assert.Equal(1, handler.RevisionCallCount);
        Assert.Equal(1, handler.DownloadCallCount);
        Assert.Null(repository.Replacement);
        Assert.Contains("TimeoutException", repository.FailureSummary);
        Assert.Equal(1, refreshLock.DisposeCount);
    }

    private static AirportCatalogRefreshService CreateService(
        FixtureRepository repository,
        FixtureLock refreshLock,
        HttpMessageHandler handler,
        int downloadTimeoutSeconds = 60)
    {
        var options = Options.Create(new AirportCatalogOptions
        {
            SourceUrl = "https://example.test/airports.csv",
            RevisionApiUrl = "https://example.test/revision",
            RevisionDownloadUrlTemplate = "https://example.test/raw/{revision}/airports.csv",
            DownloadTimeoutSeconds = downloadTimeoutSeconds,
            MinimumAirportCount = 1,
            RequiredIataCodes = ["DUB"]
        });
        return new(
            new HttpClient(handler),
            repository,
            new AirportCatalogCsvParser(),
            new AirportCatalogValidator(options),
            refreshLock,
            options,
            new FixedTimeProvider(Now),
            NullLogger<AirportCatalogRefreshService>.Instance);
    }

    private static AirportCatalogMetadata Metadata(DateTimeOffset importedAt, string checksum, string? revision = null) => new()
    {
        SourceName = "source",
        SourceUrl = "https://example.test/airports.csv",
        SourceRevision = revision ?? RevisionA,
        SourceChecksum = checksum,
        LastSuccessfulImportAt = importedAt,
        LastAttemptedRefreshAt = importedAt,
        ImportedRowCount = 1
    };

    private sealed class FixtureRepository : IAirportCatalogRepository
    {
        public AirportCatalogMetadata? Metadata { get; set; }
        public IReadOnlyCollection<AirportCatalogEntry>? Replacement { get; private set; }
        public AirportCatalogImportSource? ReplacementSource { get; private set; }
        public DateTimeOffset? UnchangedAt { get; private set; }
        public AirportCatalogImportSource? UnchangedSource { get; private set; }
        public string? FailureSummary { get; private set; }
        public bool LiveCatalogIntact { get; set; } = true;
        public bool HasStagingRows { get; set; }
        public int AbandonedStagingRowsToDelete { get; set; }
        public int DeleteAbandonedStagingCallCount { get; private set; }

        public Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIataCodesAsync(IEnumerable<string> iataCodes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIdentifiersAsync(IEnumerable<string> identifiers, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AirportCatalogMetadata?> GetMetadataAsync(CancellationToken cancellationToken) => Task.FromResult(Metadata);
        public Task<bool> IsLiveCatalogIntactAsync(int expectedRowCount, IReadOnlyCollection<string> requiredIataCodes, CancellationToken cancellationToken) => Task.FromResult(LiveCatalogIntact);
        public Task<bool> HasStagingRowsAsync(CancellationToken cancellationToken) => Task.FromResult(HasStagingRows);
        public Task<int> DeleteAbandonedStagingAsync(CancellationToken cancellationToken)
        {
            DeleteAbandonedStagingCallCount++;
            HasStagingRows = false;
            return Task.FromResult(AbandonedStagingRowsToDelete);
        }
        public Task ReplaceAsync(IReadOnlyCollection<AirportCatalogEntry> airports, AirportCatalogImportSource source, DateTimeOffset importedAt, CancellationToken cancellationToken)
        {
            Replacement = airports;
            ReplacementSource = source;
            return Task.CompletedTask;
        }
        public Task RecordUnchangedAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, CancellationToken cancellationToken)
        {
            UnchangedSource = source;
            UnchangedAt = attemptedAt;
            return Task.CompletedTask;
        }
        public Task RecordFailureAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, string summary, CancellationToken cancellationToken)
        {
            FailureSummary = summary;
            return Task.CompletedTask;
        }
    }

    private sealed class FixtureLock(bool acquired = true) : IAirportCatalogRefreshLock
    {
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult<IAsyncDisposable?>(acquired ? new Lease(this) : null);
        }
        private sealed class Lease(FixtureLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.DisposeCount++;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly string? _content;
        private readonly Exception? _exception;
        private readonly string _revision;
        public int CallCount { get; private set; }
        public int RevisionCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public string? LastDownloadUrl { get; private set; }
        public FixtureHandler(string content, string? revision = null)
        {
            _content = content;
            _revision = revision ?? RevisionB;
        }
        public FixtureHandler(Exception exception)
        {
            _exception = exception;
            _revision = RevisionB;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_exception is not null) return Task.FromException<HttpResponseMessage>(_exception);
            if (request.RequestUri?.AbsolutePath == "/revision")
            {
                RevisionCallCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"[{{\"sha\":\"{_revision}\"}}]", Encoding.UTF8, "application/json")
                });
            }

            DownloadCallCount++;
            LastDownloadUrl = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content!, Encoding.UTF8, "text/csv")
            });
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The request should only finish by cancellation.");
        }
    }

    private sealed class WaitingDownloadHandler : HttpMessageHandler
    {
        public int RevisionCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/revision")
            {
                RevisionCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"[{{\"sha\":\"{RevisionB}\"}}]", Encoding.UTF8, "application/json")
                };
            }

            DownloadCallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The CSV request should only finish by cancellation.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
