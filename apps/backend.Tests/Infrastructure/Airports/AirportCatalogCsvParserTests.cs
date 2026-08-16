using backend.Infrastructure.Airports;
using Microsoft.Extensions.Options;
using System.Text;
using Xunit;

namespace backend.Tests.Infrastructure.Airports;

public sealed class AirportCatalogCsvParserTests
{
    private static readonly DateTimeOffset ImportedAt = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_HandlesQuotedCommasUnicodeNormalizationAndBlankOptionalFields()
    {
        var result = Parse("""
            icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid
             eidw , dub ,"Dublin, International",Dublin,,ie,242,53.4213,-6.2701,Europe/Dublin,
            eham,ams,Schiphol,Amstérdam,NH,nl,,52.3086,4.7639,Europe/Amsterdam,
            """);

        Assert.Equal(2, result.Airports.Count);
        var dublin = Assert.Single(result.Airports, airport => airport.Iata == "DUB");
        Assert.Equal("EIDW", dublin.Icao);
        Assert.Equal("Dublin, International", dublin.Name);
        Assert.Null(dublin.Subdivision);
        Assert.Equal(242, dublin.ElevationFeet);
        Assert.Equal(ImportedAt, dublin.SourceUpdatedAt);
        Assert.Contains(result.Airports, airport => airport.City == "Amstérdam");
    }

    [Fact]
    public void Parse_SkipsNoIataRowsAndRejectsInvalidCandidateRows()
    {
        var result = Parse("""
            icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid
            TEST,,No Iata,Nowhere,,IE,0,1,1,Etc/UTC,
            TEST,12A,Bad code,Nowhere,,IE,0,1,1,Etc/UTC,
            TEST,BAD,Bad coordinates,Nowhere,,IE,0,91,1,Etc/UTC,
            TEST,ORG,Origin,City,,IE,0,53,-6,Europe/Dublin,
            """);

        Assert.Equal("ORG", Assert.Single(result.Airports).Iata);
        Assert.Equal(2, result.RejectedRows);
    }

    [Fact]
    public void Parse_RejectsDuplicateNormalizedIataCodes()
    {
        var exception = Assert.Throws<AirportCatalogImportException>(() => Parse("""
            icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid
            EIDW,dub,Dublin,Dublin,,IE,0,53,-6,Europe/Dublin,
            TEST,DUB,Duplicate,Dublin,,IE,0,54,-7,Europe/Dublin,
            """));

        Assert.Contains("duplicate IATA code DUB", exception.Message);
    }

    [Fact]
    public void Parse_RejectsMissingRequiredHeaders()
    {
        var exception = Assert.Throws<AirportCatalogImportException>(() => Parse("iata,name\nDUB,Dublin\n"));
        Assert.Contains("missing columns", exception.Message);
    }

    [Fact]
    public void Parse_RejectsMalformedQuotedData()
    {
        var exception = Assert.Throws<AirportCatalogImportException>(() => Parse("""
            icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid
            EIDW,DUB,Dub"lin,Dublin,,IE,242,53.4213,-6.2701,Europe/Dublin,
            """));

        Assert.Contains("malformed quoted data", exception.Message);
    }

    [Fact]
    public void Parse_AcceptsSchemaBoundariesAndRejectsOversizedDatabaseFields()
    {
        var nameAtLimit = new string('N', 256);
        var cityAtLimit = new string('C', 128);
        var textAtLimit = new string('T', 128);
        var oversizedName = new string('N', 257);
        var oversizedText = new string('T', 129);
        var result = Parse(string.Join('\n',
            "icao,iata,name,city,subd,country,elevation,lat,lon,tz,lid",
            $"EIDW,DUB,{nameAtLimit},{cityAtLimit},{textAtLimit},IE,242,53.4213,-6.2701,{textAtLimit},",
            $"TEST,NAM,{oversizedName},City,,IE,0,1,1,Etc/UTC,",
            $"TEST,CTY,Name,{oversizedText},,IE,0,1,1,Etc/UTC,",
            $"TEST,SUB,Name,City,{oversizedText},IE,0,1,1,Etc/UTC,",
            $"TEST,TZN,Name,City,,IE,0,1,1,{oversizedText},"));

        Assert.Equal("DUB", Assert.Single(result.Airports).Iata);
        Assert.Equal(4, result.RejectedRows);
    }

    private static AirportCatalogParseResult Parse(string csv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return new AirportCatalogCsvParser().Parse(stream, ImportedAt);
    }
}

public sealed class AirportCatalogValidatorTests
{
    [Fact]
    public void Validate_RejectsMissingRequiredHubs()
    {
        var validator = CreateValidator(minimumCount: 1, required: ["DUB", "AMS"]);
        var exception = Assert.Throws<AirportCatalogImportException>(() => validator.Validate([Airport("DUB")], 0));
        Assert.Contains("AMS", exception.Message);
    }

    [Fact]
    public void Validate_RejectsUnexpectedDropFromPreviousImport()
    {
        var validator = CreateValidator(minimumCount: 1, required: ["DUB"]);
        var airports = Enumerable.Range(0, 89).Select(index => Airport(index == 0 ? "DUB" : Code(index))).ToArray();
        var exception = Assert.Throws<AirportCatalogImportException>(() => validator.Validate(airports, 100));
        Assert.Contains("beyond the 10% safety limit", exception.Message);
    }

    [Fact]
    public void Validate_AcceptsBoundaryAndRequiredHubs()
    {
        var validator = CreateValidator(minimumCount: 1, required: ["DUB"]);
        var airports = Enumerable.Range(0, 90).Select(index => Airport(index == 0 ? "DUB" : Code(index))).ToArray();
        validator.Validate(airports, 100);
    }

    [Fact]
    public void Validate_RejectsFractionalDropBeyondConfiguredPercentage()
    {
        var validator = CreateValidator(minimumCount: 1, required: ["DUB"]);
        var airports = Enumerable.Range(0, 90).Select(index => Airport(index == 0 ? "DUB" : Code(index))).ToArray();

        var exception = Assert.Throws<AirportCatalogImportException>(() => validator.Validate(airports, 101));

        Assert.Contains("beyond the 10% safety limit", exception.Message);
    }

    [Fact]
    public void Validate_AcceptsRoundedUpFractionalBoundary()
    {
        var validator = CreateValidator(minimumCount: 1, required: ["DUB"]);
        var airports = Enumerable.Range(0, 91).Select(index => Airport(index == 0 ? "DUB" : Code(index))).ToArray();

        validator.Validate(airports, 101);
    }

    private static AirportCatalogValidator CreateValidator(int minimumCount, string[] required) => new(
        Options.Create(new AirportCatalogOptions
        {
            MinimumAirportCount = minimumCount,
            MaximumRowDropPercent = 10,
            RequiredIataCodes = required
        }));

    private static AirportCatalogEntry Airport(string code) => new()
    {
        Iata = code,
        Name = code,
        City = code,
        CountryCode = "IE",
        Latitude = 1,
        Longitude = 1,
        SourceUpdatedAt = DateTimeOffset.UnixEpoch
    };

    private static string Code(int index) => string.Create(3, index, static (span, value) =>
    {
        span[0] = (char)('A' + ((value / 676) % 26));
        span[1] = (char)('A' + ((value / 26) % 26));
        span[2] = (char)('A' + (value % 26));
    });
}
