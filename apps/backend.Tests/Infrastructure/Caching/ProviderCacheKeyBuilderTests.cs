using backend.Infrastructure.Caching;
using backend.Infrastructure.Models;
using Xunit;

namespace backend.Tests;

public sealed class ProviderCacheKeyBuilderTests
{
    [Fact]
    public void BuildFlightApiOneWaySearchKey_NormalizesInputs()
    {
        var request = new ProviderSearchRequest(" dub ", " ams ", new DateOnly(2026, 5, 15), 2, " Premium Economy ", " eur ");

        var key = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(request);

        Assert.Equal("provider:flightapi:oneway:DUB:AMS:2026-05-15:2:premium economy:EUR", key);
    }

    [Fact]
    public void BuildFlightApiOneWaySearchKey_SeparatesCurrencies()
    {
        var eur = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(new("DUB", "AMS", new(2026, 5, 15), 1, "economy", "EUR"));
        var usd = ProviderCacheKeyBuilder.BuildFlightApiOneWaySearchKey(new("DUB", "AMS", new(2026, 5, 15), 1, "economy", "USD"));

        Assert.NotEqual(eur, usd);
    }

    [Fact]
    public void BuildFlightApiAirportLookupKey_NormalizesQuery()
    {
        var key = ProviderCacheKeyBuilder.BuildFlightApiAirportLookupKey("  Dublin Airport  ");

        Assert.Equal("provider:flightapi:airport-lookup:dublin airport", key);
    }

}
