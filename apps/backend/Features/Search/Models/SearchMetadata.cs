namespace backend.Features.Search.Models;

public record SearchMetadata(
    int SearchCombinationCount,
    int ProviderResultCount,
    int ReturnedResultCount,
    int ReturnedDirectFlightCount,
    int ReturnedOneStopFlightCount,
    int ReturnedTwoPlusStopFlightCount
);
