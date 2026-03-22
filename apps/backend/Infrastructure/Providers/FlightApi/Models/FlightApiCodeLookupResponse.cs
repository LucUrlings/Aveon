using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiCodeLookupResponse
{
    [JsonPropertyName("data")]
    public List<FlightApiCodeLookupItem> Data { get; init; } = [];
}
