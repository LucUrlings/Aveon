using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace backend.Infrastructure.Airports;

public interface IAirportCatalogRefreshService
{
    Task<AirportCatalogRefreshResult> RefreshAsync(bool force, CancellationToken cancellationToken);
}

public sealed record AirportCatalogRefreshResult(
    AirportCatalogRefreshStatus Status,
    int AirportCount,
    int RejectedRowCount,
    string? Checksum = null);

public enum AirportCatalogRefreshStatus
{
    Refreshed,
    Unchanged,
    NotDue,
    AlreadyRunning
}

public sealed class AirportCatalogRefreshService(
    HttpClient httpClient,
    IAirportCatalogRepository repository,
    AirportCatalogCsvParser parser,
    AirportCatalogValidator validator,
    IAirportCatalogRefreshLock refreshLock,
    IOptions<AirportCatalogOptions> options,
    TimeProvider timeProvider,
    ILogger<AirportCatalogRefreshService> logger) : IAirportCatalogRefreshService
{
    private const int MaximumRevisionResponseBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AirportCatalogOptions _options = options.Value;

    public async Task<AirportCatalogRefreshResult> RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var existing = await repository.GetMetadataAsync(cancellationToken);
        if (!force && await IsCurrentAndIntactAsync(existing, now, cancellationToken))
        {
            if (!await repository.HasStagingRowsAsync(cancellationToken))
            {
                logger.LogDebug(
                    "Airport catalogue refresh is not due; {AirportCount} airports were last confirmed at {LastSuccessfulImportAt}",
                    existing!.ImportedRowCount,
                    existing.LastSuccessfulImportAt);
                return new(AirportCatalogRefreshStatus.NotDue, existing.ImportedRowCount, 0, existing.SourceChecksum);
            }

            logger.LogWarning("Airport catalogue metadata is current but abandoned staging rows require lock-protected cleanup");
        }

        await using var lease = await refreshLock.TryAcquireAsync(cancellationToken);
        if (lease is null)
        {
            logger.LogInformation("Airport catalogue refresh skipped because another backend process holds the PostgreSQL refresh lock");
            return new(AirportCatalogRefreshStatus.AlreadyRunning, existing?.ImportedRowCount ?? 0, 0, existing?.SourceChecksum);
        }

        var source = new AirportCatalogImportSource(_options.SourceName, _options.SourceUrl, null, string.Empty);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var deletedStagingRows = await repository.DeleteAbandonedStagingAsync(cancellationToken);
            if (deletedStagingRows > 0)
                logger.LogWarning("Removed {StagingRowCount} abandoned airport catalogue staging rows", deletedStagingRows);

            existing = await repository.GetMetadataAsync(cancellationToken);
            if (!force && await IsCurrentAndIntactAsync(existing, now, cancellationToken))
                return new(AirportCatalogRefreshStatus.NotDue, existing!.ImportedRowCount, 0, existing.SourceChecksum);

            var revision = await GetLatestRevisionAsync(cancellationToken);
            var pinnedSourceUrl = BuildRevisionDownloadUrl(revision);
            var liveCatalogIntact = existing is not null
                && await repository.IsLiveCatalogIntactAsync(existing.ImportedRowCount, _options.RequiredIataCodes, cancellationToken);
            source = new(_options.SourceName, pinnedSourceUrl, revision, existing?.SourceChecksum ?? string.Empty);
            if (!force
                && existing is not null
                && liveCatalogIntact
                && string.Equals(existing.SourceRevision, revision, StringComparison.OrdinalIgnoreCase))
            {
                await repository.RecordUnchangedAsync(source, now, cancellationToken);
                logger.LogInformation("Airport catalogue file revision {Revision} is unchanged; skipped the CSV download and next check is due in {RefreshAgeDays} days", revision, _options.RefreshAgeDays);
                return new(AirportCatalogRefreshStatus.Unchanged, existing.ImportedRowCount, 0, existing.SourceChecksum);
            }

            var download = await DownloadAsync(pinnedSourceUrl, cancellationToken);
            source = source with { Checksum = download.Checksum };
            if (!force
                && existing is not null
                && liveCatalogIntact
                && string.Equals(existing.SourceChecksum, download.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                await repository.RecordUnchangedAsync(source, now, cancellationToken);
                logger.LogInformation("Airport catalogue revision changed to {Revision} but its checksum {Checksum} is unchanged; retained live rows and next check is due in {RefreshAgeDays} days", revision, download.Checksum, _options.RefreshAgeDays);
                return new(AirportCatalogRefreshStatus.Unchanged, existing.ImportedRowCount, 0, download.Checksum);
            }

            using var stream = new MemoryStream(download.Bytes, writable: false);
            var parsed = parser.Parse(stream, now);
            validator.Validate(parsed.Airports, existing?.ImportedRowCount ?? 0);
            await repository.ReplaceAsync(parsed.Airports, source, now, cancellationToken);
            logger.LogInformation(
                "Airport catalogue refresh completed with {AirportCount} airports and {RejectedRowCount} rejected rows in {ElapsedMilliseconds}ms; checksum {Checksum}",
                parsed.Airports.Count,
                parsed.RejectedRows,
                stopwatch.ElapsedMilliseconds,
                download.Checksum);
            return new(AirportCatalogRefreshStatus.Refreshed, parsed.Airports.Count, parsed.RejectedRows, download.Checksum);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Airport catalogue refresh failed after {ElapsedMilliseconds}ms; the previous catalogue remains active", stopwatch.ElapsedMilliseconds);
            try
            {
                await repository.RecordFailureAsync(source, now, SafeSummary(exception), CancellationToken.None);
            }
            catch (Exception metadataException)
            {
                logger.LogWarning(metadataException, "Could not record airport catalogue refresh failure metadata");
            }
            throw;
        }
    }

    private async Task<string> GetLatestRevisionAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.RevisionApiUrl);
            request.Headers.UserAgent.ParseAdd("Aveon-Airport-Catalog/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadBoundedBytesAsync(response, MaximumRevisionResponseBytes, timeout.Token, "revision response");
            var commits = JsonSerializer.Deserialize<List<GitHubCommitRevision>>(bytes, SerializerOptions);
            var revision = commits?.FirstOrDefault()?.Sha?.Trim().ToLowerInvariant();
            if (!IsValidGitRevision(revision))
                throw new AirportCatalogImportException("Airport catalogue revision lookup did not return a valid Git commit SHA.");
            return revision!;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Airport catalogue revision lookup timed out after {_options.DownloadTimeoutSeconds} seconds.", exception);
        }
        catch (JsonException exception)
        {
            throw new AirportCatalogImportException($"Airport catalogue revision response is invalid JSON: {exception.Message}");
        }
    }

    private async Task<AirportCatalogDownload> DownloadAsync(string pinnedSourceUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pinnedSourceUrl);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadBoundedBytesAsync(response, _options.MaximumDownloadBytes, timeout.Token, "download");
            if (bytes.Length == 0) throw new AirportCatalogImportException("Airport catalogue download is empty.");
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new(bytes, checksum);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Airport catalogue download timed out after {_options.DownloadTimeoutSeconds} seconds.", exception);
        }
    }

    private async Task<byte[]> ReadBoundedBytesAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken,
        string operation)
    {
        if (response.Content.Headers.ContentLength is > 0 and var contentLength && contentLength > maximumBytes)
            throw new AirportCatalogImportException($"Airport catalogue {operation} exceeds {maximumBytes} bytes.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
                throw new AirportCatalogImportException($"Airport catalogue {operation} exceeds {maximumBytes} bytes.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    private string BuildRevisionDownloadUrl(string revision) =>
        _options.RevisionDownloadUrlTemplate.Replace("{revision}", revision, StringComparison.Ordinal);

    private async Task<bool> IsCurrentAndIntactAsync(
        AirportCatalogMetadata? metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (metadata?.LastSuccessfulImportAt is not { } importedAt
            || !IsValidGitRevision(metadata.SourceRevision)
            || now - importedAt >= TimeSpan.FromDays(_options.RefreshAgeDays))
            return false;

        var isIntact = await repository.IsLiveCatalogIntactAsync(
            metadata.ImportedRowCount,
            _options.RequiredIataCodes,
            cancellationToken);
        if (!isIntact)
            logger.LogWarning("Airport catalogue metadata is current but the live catalogue failed its row-count or required-hub integrity check; forcing a guarded refresh");
        return isIntact;
    }

    private static bool IsValidGitRevision(string? revision) =>
        revision is not null
        && revision.Length is 40 or 64
        && revision.All(Uri.IsHexDigit);

    private static string SafeSummary(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    private sealed record AirportCatalogDownload(byte[] Bytes, string Checksum);

    private sealed record GitHubCommitRevision(string? Sha);
}
