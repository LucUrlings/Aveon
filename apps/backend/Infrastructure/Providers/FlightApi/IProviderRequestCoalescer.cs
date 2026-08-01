namespace backend.Infrastructure.Providers.FlightApi;

public interface IProviderRequestCoalescer
{
    Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken cancellationToken)
        where T : class;
}
