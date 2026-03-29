using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using Xunit;

namespace backend.Tests;

public sealed class ProviderCacheKeyBuilderTests
{
    [Fact]
    public void BuildFlightApiOneWaySearchKey_NormalizesInputs()
    {
        var request = new ProviderSearchRequest(" dub ", " ams ", new DateOnly(2026, 5, 15), 2, " Premium Economy ");

        var key = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(request);

        Assert.Equal("provider:flightapi:oneway:DUB:AMS:2026-05-15:2:premium economy", key);
    }

    [Fact]
    public void BuildFlightApiAirportLookupKey_NormalizesQuery()
    {
        var key = ProviderCacheKeyBuilder.BuildFlightApiAirportLookupKey("  Dublin Airport  ");

        Assert.Equal("provider:flightapi:airport-lookup:dublin airport", key);
    }
}
