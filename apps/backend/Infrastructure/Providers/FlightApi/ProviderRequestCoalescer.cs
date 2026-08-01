using System.Collections.Concurrent;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class ProviderRequestCoalescer : IProviderRequestCoalescer
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _requests = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken cancellationToken)
        where T : class
    {
        var candidate = new Lazy<Task<object>>(
            () => ExecuteAndRemoveAsync(key, async () => await factory()),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var request = _requests.GetOrAdd(key, candidate);

        return ReferenceEquals(request, candidate)
            ? (T)await request.Value
            : (T)await request.Value.WaitAsync(cancellationToken);
    }

    private async Task<object> ExecuteAndRemoveAsync(string key, Func<Task<object>> factory)
    {
        try
        {
            return await factory();
        }
        finally
        {
            _requests.TryRemove(key, out _);
        }
    }
}
