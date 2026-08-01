using System.Diagnostics.Metrics;
using backend.Infrastructure.Providers.FlightApi.Models;
using Microsoft.Extensions.Options;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class FlightApiRequestGate : IFlightApiRequestGate, IDisposable
{
    private static readonly Meter Meter = new("Aveon.FlightApi");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("flightapi.cache_hits");
    private static readonly Counter<long> LiveCalls = Meter.CreateCounter<long>("flightapi.live_calls");
    private static readonly Counter<long> ThrottledResponses = Meter.CreateCounter<long>("flightapi.throttled_responses");

    private readonly SemaphoreSlim _gate;
    private int _activeRequests;
    private int _queuedRequests;

    public FlightApiRequestGate(IOptions<FlightApiOptions> options)
    {
        var maxConcurrentRequests = Math.Max(options.Value.MaxConcurrentRequests, 1);
        _gate = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);

        Meter.CreateObservableGauge(
            "flightapi.active_requests",
            () => Volatile.Read(ref _activeRequests));
        Meter.CreateObservableGauge(
            "flightapi.available_permits",
            () => _gate.CurrentCount);
        Meter.CreateObservableGauge(
            "flightapi.queued_requests",
            () => Volatile.Read(ref _queuedRequests));
    }

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queuedRequests);
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _queuedRequests);
        }

        Interlocked.Increment(ref _activeRequests);
        return new Lease(this);
    }

    public void RecordCacheHit() => CacheHits.Add(1);

    public void RecordLiveCall() => LiveCalls.Add(1);

    public void RecordThrottledResponse() => ThrottledResponses.Add(1);

    public void Dispose() => _gate.Dispose();

    private void Release()
    {
        Interlocked.Decrement(ref _activeRequests);
        _gate.Release();
    }

    private sealed class Lease(FlightApiRequestGate owner) : IDisposable
    {
        private FlightApiRequestGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
