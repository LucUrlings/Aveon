namespace backend.Features.Search.Models;

public record SearchStopFilterMetadata(
    int Direct,
    int OneStop,
    int TwoPlusStop
);
