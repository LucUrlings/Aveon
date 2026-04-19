namespace backend.Features.Search.Models;

public record SearchOptions
{
    public const string SectionName = "Search";

    public int MaxSearchCombinations { get; init; } = 100;
}
