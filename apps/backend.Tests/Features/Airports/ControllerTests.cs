using backend.Features.Airports;
using backend.Features.Airports.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace backend.Tests;

public sealed class AirportsControllerTests
{
    [Fact]
    public async Task Search_ReturnsOkWithServiceResponse()
    {
        var expected = new AirportLookupResponse([new AirportOption("DUB", "Dublin", "Dublin (DUB)")]);
        var controller = new AirportsController(new StubAirportService(expected));

        var result = await controller.Search("dub", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    private sealed class StubAirportService(AirportLookupResponse response) : IAirportService
    {
        public Task<AirportLookupResponse> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
