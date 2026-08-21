namespace backend.Features.Search.Models;

public record SearchOptions
{
    public const string SectionName = "Search";

    public int AnonymousMaxSearchCombinations { get; init; } = 75;

    public int UserMaxSearchCombinations { get; init; } = 200;

    public int ExecutionTimeoutMinutes { get; init; } = 10;
}
