namespace backend.Infrastructure.Models;

public record RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; init; } = "localhost:6379";

    public int FlightApiOneWayTtlMinutes { get; init; } = 30;

    public int AirportDataTtlMinutes { get; init; } = 10080;

    public int SearchSessionTtlMinutes { get; init; } = 30;
}
