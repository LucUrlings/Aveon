namespace backend.Infrastructure.Caching;

public interface IProviderResponseCache
{
    Task<T?> GetAsync<T>(string cacheKey, CancellationToken cancellationToken);

    Task SetAsync<T>(string cacheKey, T response, CancellationToken cancellationToken);
}
