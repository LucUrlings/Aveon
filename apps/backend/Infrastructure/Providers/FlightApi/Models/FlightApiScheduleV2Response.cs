using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public sealed record FlightApiScheduleV2Response
{
    [JsonPropertyName("data")]
    public FlightApiScheduleV2Data? Data { get; init; }
}

public sealed record FlightApiScheduleV2Data
{
    [JsonPropertyName("header")]
    public FlightApiScheduleV2Header? Header { get; init; }

    [JsonPropertyName("flights")]
    public List<FlightApiScheduleV2Flight>? Flights { get; init; }
}

public sealed record FlightApiScheduleV2Header
{
    [JsonPropertyName("departureAirport")]
    public FlightApiScheduleV2Airport? DepartureAirport { get; init; }
}

public sealed record FlightApiScheduleV2Flight
{
    [JsonPropertyName("airport")]
    public FlightApiScheduleV2Airport? Airport { get; init; }
}

public sealed record FlightApiScheduleV2Airport
{
    [JsonPropertyName("fs")]
    public string? Fs { get; init; }

    [JsonPropertyName("iata")]
    public string? Iata { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("country")]
    public string? Country { get; init; }
}
