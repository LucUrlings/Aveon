namespace backend.Features.Search.Models;

using System.Text.Json.Serialization;

public record SearchSessionResponse(
    string SearchId,
    string Status,
    int TotalCombinations,
    int CompletedCombinations,
    int FailedCombinations,
    SearchResponse Response,
    string? ErrorMessage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SearchStagedResults? StagedResults = null
);
