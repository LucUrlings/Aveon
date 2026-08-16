using backend.Features.Explore.Models;

namespace backend.Features.Explore;

public interface IExploreRouteCache
{
    Task<ExploreScheduleCacheEntry?> GetAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null);

    Task SetAsync(string origin, ExploreCacheProfile profile, ExploreScheduleCacheEntry entry, CancellationToken cancellationToken, DateOnly? departureDate = null);
}
