using backend.Features.Airports.Models;
using backend.Infrastructure.Providers.FlightApi;

namespace backend.Features.Airports;

public sealed class AirportService(IAirportLookupProvider airportLookupProvider) : IAirportService
{
    public async Task<AirportLookupResponse> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var trimmedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            return new AirportLookupResponse([]);
        }

        var providerResponse = await airportLookupProvider.SearchAirportsAsync(trimmedQuery, cancellationToken);

        var airports = providerResponse.Data
            .Select(item => new AirportOption(
                item.Fs,
                item.Name,
                BuildDisplayLabel(item.Name, item.Fs)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AirportLookupResponse(airports);
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
