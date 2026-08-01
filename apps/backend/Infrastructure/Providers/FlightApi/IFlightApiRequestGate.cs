namespace backend.Infrastructure.Providers.FlightApi;

/// <summary>
/// Process-wide concurrency gate for every live FlightAPI HTTP request.
/// Cache lookups must happen before acquiring a lease.
/// </summary>
public interface IFlightApiRequestGate
{
    ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken);

    void RecordCacheHit();

    void RecordLiveCall();

    void RecordThrottledResponse();
}
