using backend.Features.Search;
using backend.Features.Search.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class ProviderCallLimiterTests
{
    [Fact]
    public async Task AcquireAsync_WaitsUntilTheSharedProviderSlotIsReleased()
    {
        using var limiter = new ProviderCallLimiter(Options.Create(new SearchOptions
        {
            MaxConcurrentProviderCalls = 1
        }));
        var firstLease = await limiter.AcquireAsync(CancellationToken.None);

        var secondLeaseTask = limiter.AcquireAsync(CancellationToken.None).AsTask();

        Assert.False(secondLeaseTask.IsCompleted);

        firstLease.Dispose();
        using var secondLease = await secondLeaseTask.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
