using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiPricingOption
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("agent_ids")]
    public List<string> AgentIds { get; init; } = [];

    [JsonPropertyName("price")]
    public FlightApiPrice? Price { get; init; }

    [JsonPropertyName("items")]
    public List<FlightApiPricingItem> Items { get; init; } = [];
}
