using backend.Features.ItinerarySearch.Models;
namespace backend.Features.ItinerarySearch;

public interface IItinerarySearchSessionStore
{
    Task SetAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken);
    Task<bool> TrySetUnlessCanceledAsync(ItinerarySearchSessionResponse session, CancellationToken cancellationToken);
    Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, CancellationToken cancellationToken);
}
