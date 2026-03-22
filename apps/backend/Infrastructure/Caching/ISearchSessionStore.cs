using backend.Features.Search.Models;

namespace backend.Infrastructure.Caching;

public interface ISearchSessionStore
{
    Task<SearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken);

    Task SetAsync(SearchSessionResponse session, CancellationToken cancellationToken);
}
