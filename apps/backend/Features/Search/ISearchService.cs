using backend.Features.Search.Models;

namespace backend.Features.Search;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
}
