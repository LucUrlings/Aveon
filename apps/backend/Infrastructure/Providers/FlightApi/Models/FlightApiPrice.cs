using System.Text.Json.Serialization;

namespace backend.Infrastructure.Providers.FlightApi.Models;

public record FlightApiPrice
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }
}
