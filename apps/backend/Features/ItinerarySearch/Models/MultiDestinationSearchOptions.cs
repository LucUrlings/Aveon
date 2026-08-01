namespace backend.Features.ItinerarySearch.Models;

public record MultiDestinationSearchOptions
{
    public const string SectionName = "MultiDestinationSearch";
    public bool Enabled { get; init; }
    public int SessionTtlMinutes { get; init; } = 30;
    public int MaxOptimizedDestinations { get; init; } = 5;
    public int MaxAirportsPerGroup { get; init; } = 5;
    public int MaxTripDays { get; init; } = 31;
    public int MaxOrderedLegs { get; init; } = 8;
    public int AnonymousMaxProviderCalls { get; init; } = 25;
    public int UserMaxProviderCalls { get; init; } = 100;
    public int AdminMaxProviderCalls { get; init; } = 250;
    public int HardMaxProviderCalls { get; init; } = 500;
    public int MaxActiveStates { get; init; } = 10_000;
    public int MaxCandidatesPerState { get; init; } = 25;
    public int MaxStoredResults { get; init; } = 100;
    public int ExecutionTimeoutMinutes { get; init; } = 10;
}

public static class MultiDestinationSearchOptionsValidation
{
    public static bool HasPositiveLimits(MultiDestinationSearchOptions options) =>
        options.SessionTtlMinutes > 0 &&
        options.MaxOptimizedDestinations > 0 &&
        options.MaxAirportsPerGroup > 0 &&
        options.MaxTripDays > 0 &&
        options.MaxOrderedLegs > 0 &&
        options.AnonymousMaxProviderCalls > 0 &&
        options.UserMaxProviderCalls > 0 &&
        options.AdminMaxProviderCalls > 0 &&
        options.HardMaxProviderCalls > 0 &&
        options.MaxActiveStates > 0 &&
        options.MaxCandidatesPerState > 0 &&
        options.MaxStoredResults > 0 &&
        options.ExecutionTimeoutMinutes > 0;

    public static bool HasOrderedProviderBudgets(MultiDestinationSearchOptions options) =>
        options.AnonymousMaxProviderCalls <= options.UserMaxProviderCalls &&
        options.UserMaxProviderCalls <= options.AdminMaxProviderCalls &&
        options.AdminMaxProviderCalls <= options.HardMaxProviderCalls;
}
