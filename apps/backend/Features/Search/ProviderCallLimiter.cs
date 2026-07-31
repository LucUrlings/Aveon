using backend.Features.Search.Models;
using Microsoft.Extensions.Options;

namespace backend.Features.Search;

public sealed class ProviderCallLimiter : IProviderCallLimiter, IDisposable
{
    private readonly SemaphoreSlim _gate;

    public ProviderCallLimiter(IOptions<SearchOptions> options)
    {
        var maxConcurrentCalls = Math.Max(options.Value.MaxConcurrentProviderCalls, 1);
        _gate = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
    }

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new ProviderCallLease(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class ProviderCallLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
