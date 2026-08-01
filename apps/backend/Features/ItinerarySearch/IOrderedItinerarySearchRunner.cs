using backend.Features.ItinerarySearch.Models;

namespace backend.Features.ItinerarySearch;

public interface IOrderedItinerarySearchRunner
{
    Task RunAsync(string searchId, OrderedTripRequest request, int providerCallLimit, CancellationToken cancellationToken);
}
