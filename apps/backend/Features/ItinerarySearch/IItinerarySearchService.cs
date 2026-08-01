using backend.Features.ItinerarySearch.Models;

namespace backend.Features.ItinerarySearch;

public interface IItinerarySearchService
{
    Task<ItinerarySearchSessionResponse> StartAsync(ItinerarySearchRequest request, int providerCallLimit, CancellationToken cancellationToken);
    Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, ItineraryResultsQuery query, CancellationToken cancellationToken);
    Task<ItinerarySearchSessionResponse?> CancelAsync(string searchId, CancellationToken cancellationToken);
}

public sealed class ItineraryValidationException(string field, string message) : ArgumentException(message)
{
    public string Field { get; } = field;
}
