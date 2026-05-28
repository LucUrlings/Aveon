using backend.Features.Search.Models;

namespace backend.Features.Search;

public interface ISearchService
{
    Task<SearchSessionResponse> StartSearchAsync(SearchRequest request, SearchLimit searchLimit, CancellationToken cancellationToken);

    Task<SearchSessionResponse?> GetSearchAsync(string searchId, SearchResultsQuery query, CancellationToken cancellationToken);
}
