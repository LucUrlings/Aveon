using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiOneWayResponse
{
    [JsonPropertyName("itineraries")]
    public List<FlightApiItinerary> Itineraries { get; init; } = [];

    [JsonPropertyName("legs")]
    public List<FlightApiLeg> Legs { get; init; } = [];

    [JsonPropertyName("segments")]
    public List<FlightApiSegment> Segments { get; init; } = [];

    [JsonPropertyName("places")]
    public List<FlightApiPlace> Places { get; init; } = [];

    [JsonPropertyName("carriers")]
    public List<FlightApiCarrier> Carriers { get; init; } = [];

    [JsonPropertyName("agents")]
    public List<FlightApiAgent> Agents { get; init; } = [];
}
