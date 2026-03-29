using backend.Features.Search.Models;
using Xunit;

namespace backend.Tests;

public sealed class SearchRequestTests
{
    [Fact]
    public void GetDepartureDates_ReturnsDistinctDatesInAscendingOrder()
    {
        var request = new SearchRequest(
            ["DUB"],
            ["AMS"],
            [new DateOnly(2026, 5, 17), new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 17)],
            null,
            null,
            1,
            "economy");

        var dates = request.GetDepartureDates().ToArray();

        Assert.Equal(
            [new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 17)],
            dates);
    }
}
