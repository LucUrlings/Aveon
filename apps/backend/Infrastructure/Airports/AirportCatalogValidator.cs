using Microsoft.Extensions.Options;

namespace backend.Infrastructure.Airports;

public sealed class AirportCatalogValidator(IOptions<AirportCatalogOptions> options)
{
    private readonly AirportCatalogOptions _options = options.Value;

    public void Validate(IReadOnlyCollection<AirportCatalogEntry> airports, int previousCount)
    {
        if (airports.Count < _options.MinimumAirportCount)
            throw new AirportCatalogImportException($"Airport catalogue contains only {airports.Count} valid airports; expected at least {_options.MinimumAirportCount}.");
        if (previousCount > 0)
        {
            var retainedPercent = 100 - _options.MaximumRowDropPercent;
            var minimumFromPrevious = (int)(((long)previousCount * retainedPercent + 99) / 100);
            if (airports.Count < minimumFromPrevious)
                throw new AirportCatalogImportException($"Airport catalogue dropped from {previousCount} to {airports.Count} rows, beyond the {_options.MaximumRowDropPercent}% safety limit.");
        }

        var available = airports.Select(airport => airport.Iata).ToHashSet(StringComparer.Ordinal);
        var missingHubs = _options.RequiredIataCodes.Select(code => code.Trim().ToUpperInvariant()).Where(code => !available.Contains(code)).ToArray();
        if (missingHubs.Length > 0)
            throw new AirportCatalogImportException($"Airport catalogue is missing required hubs: {string.Join(", ", missingHubs)}.");
    }
}
