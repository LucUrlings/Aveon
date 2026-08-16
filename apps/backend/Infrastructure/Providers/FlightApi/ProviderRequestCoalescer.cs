using System.Collections.Concurrent;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class ProviderRequestCoalescer : IProviderRequestCoalescer
{
    private readonly ConcurrentDictionary<string, RequestEntry> _requests = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequestEntry request;
        while (true)
        {
            var candidate = new RequestEntry((entry, sharedCancellationToken) =>
                ExecuteAndRemoveAsync(
                    key,
                    entry,
                    async () => await factory(sharedCancellationToken)));
            request = _requests.GetOrAdd(key, candidate);
            if (!ReferenceEquals(request, candidate)) candidate.DisposeUnused();

            if (request.TryAddWaiter()) break;

            if (_requests.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                _requests.TryRemove(key, out _);
        }

        try
        {
            return (T)await request.Work.WaitAsync(cancellationToken);
        }
        finally
        {
            request.RemoveWaiter();
        }
    }

    private async Task<object> ExecuteAndRemoveAsync(
        string key,
        RequestEntry request,
        Func<Task<object>> factory)
    {
        try
        {
            return await factory();
        }
        finally
        {
            request.MarkCompleted();
            if (_requests.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                _requests.TryRemove(key, out _);
        }
    }

    private sealed class RequestEntry
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _sharedCancellation = new();
        private readonly Lazy<Task<object>> _work;
        private int _waiterCount;
        private bool _completed;
        private bool _abandoned;
        private bool _canceling;
        private bool _disposed;

        public RequestEntry(Func<RequestEntry, CancellationToken, Task<object>> factory)
        {
            _work = new(
                () => factory(this, _sharedCancellation.Token),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Task<object> Work => _work.Value;

        public bool TryAddWaiter()
        {
            lock (_sync)
            {
                if (_completed || _abandoned) return false;
                _waiterCount++;
                return true;
            }
        }

        public void RemoveWaiter()
        {
            var cancel = false;
            var dispose = false;
            lock (_sync)
            {
                _waiterCount--;
                cancel = _waiterCount == 0 && !_completed;
                _abandoned = cancel;
                _canceling = cancel;
                dispose = _waiterCount == 0 && _completed;
            }

            if (cancel)
            {
                try
                {
                    _sharedCancellation.Cancel();
                }
                catch (AggregateException)
                {
                    // Cancellation callbacks belong to the shared operation; waiter cleanup must still complete.
                }
                finally
                {
                    lock (_sync)
                    {
                        _canceling = false;
                        dispose = _waiterCount == 0 && _completed;
                    }
                }
            }
            if (dispose) DisposeCore();
        }

        public void MarkCompleted()
        {
            var dispose = false;
            lock (_sync)
            {
                _completed = true;
                dispose = _waiterCount == 0 && !_canceling;
            }
            if (dispose) DisposeCore();
        }

        public void DisposeUnused()
        {
            lock (_sync)
            {
                _completed = true;
            }
            DisposeCore();
        }

        private void DisposeCore()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _sharedCancellation.Dispose();
        }
    }
}
