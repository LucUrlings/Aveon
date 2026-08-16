using System.Collections.Concurrent;
using System.Threading.Channels;

namespace backend.Features.Explore;

public interface IHeroRouteWarmupQueue
{
    bool TryEnqueue(string origin);

    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);

    void MarkComplete(string origin);
}

public sealed class HeroRouteWarmupQueue : IHeroRouteWarmupQueue
{
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public bool TryEnqueue(string origin)
    {
        var normalized = origin.Trim().ToUpperInvariant();
        if (normalized.Length != 3
            || normalized.Any(character => character is < 'A' or > 'Z')
            || !_pending.TryAdd(normalized, 0))
            return false;
        if (_queue.Writer.TryWrite(normalized)) return true;
        _pending.TryRemove(normalized, out _);
        return false;
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    public void MarkComplete(string origin) => _pending.TryRemove(origin.Trim().ToUpperInvariant(), out _);
}

public sealed class HeroRouteWarmupWorker(
    IServiceScopeFactory scopeFactory,
    IHeroRouteWarmupQueue queue,
    ILogger<HeroRouteWarmupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var origin in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var routeService = scope.ServiceProvider.GetRequiredService<IExploreRouteService>();
                    var response = await routeService.GetRoutesAsync(origin, Models.ExploreCacheProfile.Hero, stoppingToken);
                    if (response.IsComplete)
                        logger.LogInformation("Completed background homepage route warmup for {Origin}", origin);
                    else
                        logger.LogWarning("Background homepage route warmup for {Origin} returned a partial network; a later homepage selection may retry it", origin);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Background homepage route warmup failed for {Origin}", origin);
                }
                finally
                {
                    queue.MarkComplete(origin);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
