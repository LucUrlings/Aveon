using backend.Features.Explore.Models;

namespace backend.Features.Explore;

public interface IExploreRouteCache
{
    Task<ExploreRouteCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken);

    Task SetAsync(string origin, ExploreCacheProfile profile, ExploreRouteCacheEntry entry, CancellationToken cancellationToken);
}
