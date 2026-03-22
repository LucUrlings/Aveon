using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiItinerary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("leg_ids")]
    public List<string> LegIds { get; init; } = [];

    [JsonPropertyName("pricing_options")]
    public List<FlightApiPricingOption> PricingOptions { get; init; } = [];

    [JsonPropertyName("deepLink")]
    public string? DeepLink { get; init; }
}
