using backend.Features.Explore;
using backend.Features.Explore.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace backend.Tests;

public sealed class HeroRouteWarmupWorkerTests
{
    [Fact]
    public async Task QueuedHub_IsWarmedSequentiallyThroughScopedRouteService()
    {
        var routeService = new CapturingRouteService();
        var services = new ServiceCollection()
            .AddSingleton(routeService)
            .AddScoped<IExploreRouteService>(provider => provider.GetRequiredService<CapturingRouteService>())
            .BuildServiceProvider();
        var queue = new HeroRouteWarmupQueue();
        var worker = new HeroRouteWarmupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<HeroRouteWarmupWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(queue.TryEnqueue(" dxb "));
            var call = await routeService.Call.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal("DXB", call.Origin);
            Assert.Equal(ExploreCacheProfile.Hero, call.Profile);
            var requeued = false;
            for (var attempt = 0; attempt < 100 && !requeued; attempt += 1)
            {
                requeued = queue.TryEnqueue("DXB");
                if (!requeued) await Task.Delay(10);
            }
            Assert.True(requeued);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    private sealed class CapturingRouteService : IExploreRouteService
    {
        public TaskCompletionSource<(string Origin, ExploreCacheProfile Profile)> Call { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExploreRoutesResponse> GetRoutesAsync(
            string origin,
            ExploreCacheProfile profile,
            CancellationToken cancellationToken,
            DateOnly? departureDate = null)
        {
            Call.TrySetResult((origin, profile));
            return Task.FromResult(new ExploreRoutesResponse(
                new(origin, origin, origin, "United Arab Emirates", 25.25, 55.36),
                [],
                new DateOnly(2026, 8, 4),
                new DateOnly(2026, 8, 14),
                new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
                true,
                false));
        }
    }
}
