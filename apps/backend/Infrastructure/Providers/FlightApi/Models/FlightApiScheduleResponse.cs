using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public sealed record FlightApiScheduleResponse
{
    [JsonPropertyName("airport")]
    public FlightApiScheduleAirport? Airport { get; init; }
}

public sealed record FlightApiScheduleAirport
{
    [JsonPropertyName("pluginData")]
    public FlightApiSchedulePluginData? PluginData { get; init; }
}

public sealed record FlightApiSchedulePluginData
{
    [JsonPropertyName("details")]
    public FlightApiScheduleAirportDetails? Details { get; init; }

    [JsonPropertyName("schedule")]
    public FlightApiScheduleCollection? Schedule { get; init; }
}

public sealed record FlightApiScheduleAirportDetails
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("code")]
    public FlightApiScheduleCode? Code { get; init; }

    [JsonPropertyName("position")]
    public FlightApiSchedulePosition? Position { get; init; }
}

public sealed record FlightApiScheduleCollection
{
    [JsonPropertyName("departures")]
    public FlightApiScheduleDepartures? Departures { get; init; }
}

public sealed record FlightApiScheduleDepartures
{
    [JsonPropertyName("page")]
    public FlightApiSchedulePage? Page { get; init; }

    [JsonPropertyName("data")]
    public List<FlightApiScheduleRow>? Data { get; init; }
}

public sealed record FlightApiSchedulePage
{
    [JsonPropertyName("current")]
    public int Current { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

public sealed record FlightApiScheduleRow
{
    [JsonPropertyName("flight")]
    public FlightApiScheduleFlight? Flight { get; init; }
}

public sealed record FlightApiScheduleFlight
{
    [JsonPropertyName("airport")]
    public FlightApiScheduleFlightAirports? Airport { get; init; }
}

public sealed record FlightApiScheduleFlightAirports
{
    [JsonPropertyName("destination")]
    public FlightApiScheduleDestination? Destination { get; init; }
}

public sealed record FlightApiScheduleDestination
{
    [JsonPropertyName("code")]
    public FlightApiScheduleCode? Code { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("position")]
    public FlightApiSchedulePosition? Position { get; init; }
}

public sealed record FlightApiScheduleCode
{
    [JsonPropertyName("iata")]
    public string? Iata { get; init; }
}

public sealed record FlightApiSchedulePosition
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    [JsonPropertyName("country")]
    public FlightApiScheduleCountry? Country { get; init; }

    [JsonPropertyName("region")]
    public FlightApiScheduleRegion? Region { get; init; }
}

public sealed record FlightApiScheduleCountry
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed record FlightApiScheduleRegion
{
    [JsonPropertyName("city")]
    public string? City { get; init; }
}
