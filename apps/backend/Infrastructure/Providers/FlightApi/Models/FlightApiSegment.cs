using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiSegment
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("origin_place_id")]
    public int OriginPlaceId { get; init; }

    [JsonPropertyName("destination_place_id")]
    public int DestinationPlaceId { get; init; }

    [JsonPropertyName("arrival")]
    public DateTime Arrival { get; init; }

    [JsonPropertyName("departure")]
    public DateTime Departure { get; init; }

    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    [JsonPropertyName("marketing_flight_number")]
    public string? MarketingFlightNumber { get; init; }

    [JsonPropertyName("marketing_carrier_id")]
    public int? MarketingCarrierId { get; init; }

    [JsonPropertyName("operating_carrier_id")]
    public int? OperatingCarrierId { get; init; }
}
