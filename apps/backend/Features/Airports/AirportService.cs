using backend.Features.Airports.Models;
using backend.Infrastructure.Airports;
using backend.Infrastructure.Providers.FlightApi;

namespace backend.Features.Airports;

public sealed class AirportService(
    IAirportLookupProvider airportLookupProvider,
    IAirportCatalogRepository airportCatalog) : IAirportService
{
    public async Task<AirportLookupResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var trimmedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return new AirportLookupResponse([]);
        }

        var normalizedQuery = trimmedQuery.ToUpperInvariant();
        var exactCatalogMatches = await airportCatalog.GetByIdentifiersAsync([normalizedQuery], cancellationToken);
        if (normalizedQuery.Length == 4 && exactCatalogMatches.TryGetValue(normalizedQuery, out var exactAirport))
        {
            return new AirportLookupResponse([FromCatalog(exactAirport)]);
        }

        var providerResponse = await airportLookupProvider.SearchAirportsAsync(trimmedQuery, cancellationToken);
        var providerCodes = providerResponse.Data
            .Select(item => item.Fs?.Trim().ToUpperInvariant())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Cast<string>()
            .ToArray();
        var catalogMatches = await airportCatalog.GetByIdentifiersAsync(providerCodes.Append(normalizedQuery), cancellationToken);

        var airports = new List<AirportOption>();
        if (catalogMatches.TryGetValue(normalizedQuery, out exactAirport)) airports.Add(FromCatalog(exactAirport));

        airports.AddRange(providerResponse.Data
            .Select(item => FromProvider(item.Fs, item.Name, catalogMatches))
            .Where(item => item is not null)
            .Cast<AirportOption>()
            .DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
        );

        return new AirportLookupResponse(airports
            .DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static AirportOption? FromProvider(
        string? code,
        string? name,
        IReadOnlyDictionary<string, AirportCatalogEntry> catalogMatches)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (catalogMatches.TryGetValue(normalizedCode, out var catalogAirport)) return FromCatalog(catalogAirport);
        if (normalizedCode.Length != 3) return null;
        return new AirportOption(normalizedCode, name, BuildDisplayLabel(name, normalizedCode));
    }

    private static AirportOption FromCatalog(AirportCatalogEntry airport)
    {
        var name = airport.Name.Trim();
        var city = airport.City.Trim();
        var place = string.IsNullOrWhiteSpace(city) || name.Contains(city, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}, {city}";
        return new AirportOption(airport.Iata, name, $"{place} ({airport.Iata})");
    }

    private static string BuildDisplayLabel(string? name, string? code)
    {
        var safeCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var safeName = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return safeCode;
        }

        return $"{safeName} ({safeCode})";
    }
}
