using backend.Features.Airports;
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
        var service = new AirportService(provider);

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
        var service = new AirportService(provider);

        var response = await service.SearchAsync(" am ", CancellationToken.None);

        Assert.Equal(["ams", "DUB"], response.Airports.Select(airport => airport.Code).ToArray());
        Assert.Equal("Amsterdam Schiphol (AMS)", response.Airports[0].DisplayLabel);
        Assert.Equal("Dublin (DUB)", response.Airports[1].DisplayLabel);
        Assert.Equal("am", provider.LastQuery);
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
}
