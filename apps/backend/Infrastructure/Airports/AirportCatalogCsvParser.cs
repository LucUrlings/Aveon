using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace backend.Infrastructure.Airports;

public sealed class AirportCatalogCsvParser
{
    private const int MaximumNameLength = 256;
    private const int MaximumLocationTextLength = 128;

    public AirportCatalogParseResult Parse(Stream csvStream, DateTimeOffset sourceUpdatedAt)
    {
        using var reader = new StreamReader(csvStream, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = _ => throw new AirportCatalogImportException("Airport catalogue CSV contains malformed quoted data."),
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        });

        if (!csv.Read() || !csv.ReadHeader())
            throw new AirportCatalogImportException("Airport catalogue CSV has no header.");

        var requiredHeaders = new[] { "icao", "iata", "name", "city", "subd", "country", "elevation", "lat", "lon", "tz" };
        var headers = csv.HeaderRecord?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var missingHeaders = requiredHeaders.Where(header => !headers.Contains(header)).ToArray();
        if (missingHeaders.Length > 0)
            throw new AirportCatalogImportException($"Airport catalogue CSV is missing columns: {string.Join(", ", missingHeaders)}.");

        var airports = new Dictionary<string, AirportCatalogEntry>(StringComparer.Ordinal);
        var rejectedRows = 0;
        while (csv.Read())
        {
            var rawIata = Field(csv, "iata");
            if (string.IsNullOrWhiteSpace(rawIata)) continue;

            var iata = rawIata.ToUpperInvariant();
            var name = Field(csv, "name");
            var rawCity = Field(csv, "city");
            var city = string.IsNullOrWhiteSpace(rawCity) ? name : rawCity;
            var subdivision = NullIfEmpty(Field(csv, "subd"));
            var timezone = NullIfEmpty(Field(csv, "tz"));
            var country = Field(csv, "country").ToUpperInvariant();
            if (!IsAsciiLetters(iata, 3)
                || !IsAsciiLetters(country, 2)
                || string.IsNullOrWhiteSpace(name)
                || name.Length > MaximumNameLength
                || city.Length > MaximumLocationTextLength
                || subdivision?.Length > MaximumLocationTextLength
                || timezone?.Length > MaximumLocationTextLength
                || !TryCoordinate(Field(csv, "lat"), -90, 90, out var latitude)
                || !TryCoordinate(Field(csv, "lon"), -180, 180, out var longitude)
                || (latitude == 0 && longitude == 0))
            {
                rejectedRows++;
                continue;
            }

            if (airports.ContainsKey(iata))
                throw new AirportCatalogImportException($"Airport catalogue contains duplicate IATA code {iata}.");

            var icao = Field(csv, "icao").ToUpperInvariant();
            airports.Add(iata, new AirportCatalogEntry
            {
                Iata = iata,
                Icao = IsAsciiLettersOrDigits(icao, 4) ? icao : null,
                Name = name,
                City = city,
                Subdivision = subdivision,
                CountryCode = country,
                Latitude = latitude,
                Longitude = longitude,
                ElevationFeet = int.TryParse(Field(csv, "elevation"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elevation) ? elevation : null,
                Timezone = timezone,
                SourceUpdatedAt = sourceUpdatedAt
            });
        }

        return new(airports.Values.OrderBy(airport => airport.Iata, StringComparer.Ordinal).ToArray(), rejectedRows);
    }

    private static string Field(CsvReader csv, string name) => csv.GetField(name)?.Trim() ?? string.Empty;

    private static bool TryCoordinate(string value, double minimum, double maximum, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result)
        && result >= minimum
        && result <= maximum;

    private static bool IsAsciiLetters(string value, int length) =>
        value.Length == length && value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsAsciiLettersOrDigits(string value, int length) =>
        value.Length == length && value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9');

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed record AirportCatalogParseResult(IReadOnlyCollection<AirportCatalogEntry> Airports, int RejectedRows);

public sealed class AirportCatalogImportException(string message) : Exception(message);
