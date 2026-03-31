using backend.Features.Search.Models;

namespace backend.Features.Search;

public interface ISearchService
{
    Task<SearchSessionResponse> StartSearchAsync(SearchRequest request, CancellationToken cancellationToken);

    Task<SearchSessionResponse?> GetSearchAsync(string searchId, SearchResultsQuery query, CancellationToken cancellationToken);
}
