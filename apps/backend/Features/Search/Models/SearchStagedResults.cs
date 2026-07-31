namespace backend.Features.Search.Models;

public record SearchStagedResults(
    List<SearchResult> OutboundResults,
    List<SearchResult> InboundResults,
    List<SearchResult> RoundTripResults
);
