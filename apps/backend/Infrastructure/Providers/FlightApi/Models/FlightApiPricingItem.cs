using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiPricingItem
{
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("segment_ids")]
    public List<string> SegmentIds { get; init; } = [];

    [JsonPropertyName("price")]
    public FlightApiPrice? Price { get; init; }
}
