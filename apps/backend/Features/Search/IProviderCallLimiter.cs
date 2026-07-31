namespace backend.Features.Search;

public interface IProviderCallLimiter
{
    ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken);
}
