namespace backend.Features.Search.Models;

public record SearchResult(
    string Id,
    bool IsRoundTrip,
    List<SearchResultLeg> Legs,
    int TotalDurationMinutes,
    List<SearchResultPriceOption> PriceOptions
);
