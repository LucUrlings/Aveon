namespace backend.Features.Search.Models;

public record SearchResponse(
    List<SearchResult> Results,
    SearchMetadata Metadata,
    SearchFiltersMetadata Filters,
    SearchPagination Pagination
);
