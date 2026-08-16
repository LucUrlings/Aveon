using Microsoft.Extensions.Options;

namespace backend.Infrastructure.Airports;

public sealed class AirportCatalogRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AirportCatalogOptions> options,
    ILogger<AirportCatalogRefreshWorker> logger) : BackgroundService
{
    private readonly AirportCatalogOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RefreshEnabled)
        {
            logger.LogInformation("Automatic airport catalogue refresh is disabled");
            return;
        }

        await RefreshSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.CheckIntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshSafelyAsync(stoppingToken);
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var refresher = scope.ServiceProvider.GetRequiredService<IAirportCatalogRefreshService>();
            await refresher.RefreshAsync(force: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled airport catalogue refresh failed; it will be retried at the next maintenance check");
        }
    }
}
