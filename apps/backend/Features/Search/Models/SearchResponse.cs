namespace backend.Features.Search.Models;

public record SearchResponse(
    List<SearchResult> Results,
    SearchMetadata Metadata
);
