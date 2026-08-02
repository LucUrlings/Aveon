namespace backend.Features.Explore.Models;

public sealed record ExploreAirport(
    string Code,
    string Name,
    string City,
    string Country,
    double Latitude,
    double Longitude);

public sealed record ExploreRoutesResponse(
    ExploreAirport Origin,
    List<ExploreAirport> Destinations,
    DateOnly ObservedFrom,
    DateOnly ObservedTo,
    DateTimeOffset FetchedAt,
    bool IsComplete,
    bool IsStale);

public enum ExploreCacheProfile
{
    Explore,
    Hero
}

public sealed record ExploreRouteCacheEntry(
    ExploreRoutesResponse Response,
    DateTimeOffset FetchedAt);
