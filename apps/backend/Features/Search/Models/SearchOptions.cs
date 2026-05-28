namespace backend.Features.Search.Models;

public record SearchOptions
{
    public const string SectionName = "Search";

    public int AnonymousMaxSearchCombinations { get; init; } = 15;

    public int UserMaxSearchCombinations { get; init; } = 100;
}
