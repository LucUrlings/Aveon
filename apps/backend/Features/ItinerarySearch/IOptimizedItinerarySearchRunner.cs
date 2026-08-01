using backend.Features.ItinerarySearch.Models;

namespace backend.Features.ItinerarySearch;

public interface IOptimizedItinerarySearchRunner
{
    Task RunAsync(
        string searchId,
        OptimizedTripRequest request,
        OptimizedSchedulePlan schedulePlan,
        int providerCallLimit,
        CancellationToken cancellationToken);
}
