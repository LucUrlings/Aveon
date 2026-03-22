using System.Text.Json;
using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiAgent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}
