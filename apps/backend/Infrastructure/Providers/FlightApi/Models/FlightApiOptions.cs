namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiOptions
{
    public const string SectionName = "FlightApi";

    public string BaseUrl { get; init; } = "https://api.flightapi.io/";

    public string ApiKey { get; init; } = string.Empty;

    public string Currency { get; init; } = "EUR";
}
