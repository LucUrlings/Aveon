namespace backend.Features.Search.Models;

public record SearchSessionResponse(
    string SearchId,
    string Status,
    int TotalCombinations,
    int CompletedCombinations,
    int FailedCombinations,
    SearchResponse Response,
    string? ErrorMessage
);
