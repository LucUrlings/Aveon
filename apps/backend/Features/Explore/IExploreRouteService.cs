using backend.Features.Explore.Models;

namespace backend.Features.Explore;

public interface IExploreRouteService
{
    Task<ExploreRoutesResponse> GetRoutesAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null);

    Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken);
}
