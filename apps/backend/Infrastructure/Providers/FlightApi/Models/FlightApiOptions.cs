namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiOptions
{
    public const string SectionName = "FlightApi";

    public int MaxConcurrentRequests { get; init; } = 5;

    public int MaxRetryAttempts { get; init; } = 3;

    public int MaxRetryDelaySeconds { get; init; } = 30;

    public int MaxSchedulePages { get; init; } = 10;

    public string BaseUrl { get; init; } = "https://api.flightapi.io/";

    public string ApiKey { get; init; } = string.Empty;

    public string Currency { get; init; } = "EUR";

}
