using backend.Features.Airports;
using backend.Infrastructure.Airports;
using backend.Infrastructure.Providers.FlightApi;
using backend.Infrastructure.Providers.FlightApi.Models;
using Xunit;

namespace backend.Tests;

public sealed class AirportServiceTests
{
    [Fact]
    public async Task SearchAsync_ReturnsEmptyResponse_ForBlankQuery()
    {
        var provider = new RecordingAirportLookupProvider();
        var service = new AirportService(provider, new FixtureAirportCatalog());

        var response = await service.SearchAsync("   ", CancellationToken.None);

        Assert.Empty(response.Airports);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesAndSortsAirports()
    {
        var provider = new RecordingAirportLookupProvider
        {
            Response = new FlightApiCodeLookupResponse
            {
                Data =
                [
                    new() { Fs = "ams", Name = "Amsterdam Schiphol" },
                    new() { Fs = "DUB", Name = "Dublin" },
                    new() { Fs = "AMS", Name = "Duplicate Amsterdam" },
                    new() { Fs = "", Name = "Missing code" }
                ]
            }
        };
        var service = new AirportService(provider, new FixtureAirportCatalog());

        var response = await service.SearchAsync(" am ", CancellationToken.None);

        Assert.Equal(["AMS", "DUB"], response.Airports.Select(airport => airport.Code).ToArray());
        Assert.Equal("Amsterdam Schiphol (AMS)", response.Airports[0].DisplayLabel);
        Assert.Equal("Dublin (DUB)", response.Airports[1].DisplayLabel);
        Assert.Equal("am", provider.LastQuery);
    }

    [Fact]
    public async Task SearchAsync_ResolvesFourLetterIcaoCodeToCanonicalIataAirport()
    {
        var provider = new RecordingAirportLookupProvider();
        var service = new AirportService(provider, new FixtureAirportCatalog(
            Airport("DUB", "EIDW", "Dublin Airport", "Dublin")));

        var response = await service.SearchAsync(" eidw ", CancellationToken.None);

        var airport = Assert.Single(response.Airports);
        Assert.Equal("DUB", airport.Code);
        Assert.Equal("Dublin Airport (DUB)", airport.DisplayLabel);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task SearchAsync_CanonicalizesFourLetterProviderResults()
    {
        var provider = new RecordingAirportLookupProvider
        {
            Response = new FlightApiCodeLookupResponse
            {
                Data = [new() { Fs = "EIDW", Name = "Dublin" }]
            }
        };
        var service = new AirportService(provider, new FixtureAirportCatalog(
            Airport("DUB", "EIDW", "Dublin Airport", "Dublin")));

        var response = await service.SearchAsync("Dublin", CancellationToken.None);

        Assert.Equal("DUB", Assert.Single(response.Airports).Code);
    }

    private sealed class RecordingAirportLookupProvider : IAirportLookupProvider
    {
        public int CallCount { get; private set; }

        public string? LastQuery { get; private set; }

        public FlightApiCodeLookupResponse Response { get; init; } = new();

        public Task<FlightApiCodeLookupResponse> SearchAirportsAsync(
            string query,
            CancellationToken cancellationToken)
        {
            CallCount += 1;
            LastQuery = query;
            return Task.FromResult(Response);
        }
    }

    private sealed class FixtureAirportCatalog(params AirportCatalogEntry[] airports) : IAirportCatalogRepository
    {
        public Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIdentifiersAsync(IEnumerable<string> identifiers, CancellationToken cancellationToken)
        {
            var requested = identifiers.Select(identifier => identifier.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
            var result = new Dictionary<string, AirportCatalogEntry>(StringComparer.Ordinal);
            foreach (var airport in airports.Where(airport => requested.Contains(airport.Iata) || airport.Icao is not null && requested.Contains(airport.Icao)))
            {
                result[airport.Iata] = airport;
                if (airport.Icao is not null) result[airport.Icao] = airport;
            }
            return Task.FromResult<IReadOnlyDictionary<string, AirportCatalogEntry>>(result);
        }

        public Task<IReadOnlyDictionary<string, AirportCatalogEntry>> GetByIataCodesAsync(IEnumerable<string> iataCodes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AirportCatalogMetadata?> GetMetadataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsLiveCatalogIntactAsync(int expectedRowCount, IReadOnlyCollection<string> requiredIataCodes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasStagingRowsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteAbandonedStagingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceAsync(IReadOnlyCollection<AirportCatalogEntry> replacement, AirportCatalogImportSource source, DateTimeOffset importedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordUnchangedAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordFailureAsync(AirportCatalogImportSource source, DateTimeOffset attemptedAt, string summary, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static AirportCatalogEntry Airport(string iata, string icao, string name, string city) => new()
    {
        Iata = iata,
        Icao = icao,
        Name = name,
        City = city,
        CountryCode = "IE",
        Latitude = 53.42,
        Longitude = -6.27,
        SourceUpdatedAt = DateTimeOffset.UtcNow
    };
}
