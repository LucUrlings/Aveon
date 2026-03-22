using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiLeg
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("origin_place_id")]
    public int OriginPlaceId { get; init; }

    [JsonPropertyName("destination_place_id")]
    public int DestinationPlaceId { get; init; }

    [JsonPropertyName("departure")]
    public DateTime Departure { get; init; }

    [JsonPropertyName("arrival")]
    public DateTime Arrival { get; init; }

    [JsonPropertyName("segment_ids")]
    public List<string> SegmentIds { get; init; } = [];

    [JsonPropertyName("duration")]
    public int Duration { get; init; }
}
