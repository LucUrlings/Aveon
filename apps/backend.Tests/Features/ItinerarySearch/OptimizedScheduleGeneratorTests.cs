using backend.Features.ItinerarySearch;
using backend.Features.ItinerarySearch.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class OptimizedScheduleGeneratorTests
{
    private readonly OptimizedScheduleGenerator _generator = new(Options.Create(new MultiDestinationSearchOptions()));

    [Theory]
    [InlineData("returnToStart", 3, "start")]
    [InlineData("openEnded", 2, null)]
    [InlineData("fixedEnd", 3, "finish")]
    public void EndpointModes_GenerateOnlySchedulesWithTheRequiredTerminalRule(string endpointMode, int legCount, string? terminalId)
    {
        var request = Request(endpointMode, new DateOnly(2026, 9, 7));

        var plan = _generator.Generate(request);

        Assert.NotEmpty(plan.Schedules);
        Assert.All(plan.Schedules, schedule =>
        {
            Assert.Equal(legCount, schedule.Legs.Count);
            Assert.Equal(new DateOnly(2026, 9, 1), schedule.Legs[0].DepartureDate);
            if (terminalId is null)
            {
                Assert.Equal(request.EndDate, schedule.Stays[^1].DepartureDate);
            }
            else
            {
                Assert.Equal(terminalId, schedule.Legs[^1].ToGroupId);
                Assert.True(schedule.Legs[^1].DepartureDate <= request.EndDate);
                Assert.Equal(request.EndDate, schedule.Legs[^1].RequiredArrivalDate);
            }
        });
    }

    [Fact]
    public void MinimumStays_CanAbsorbSpareNights()
    {
        var request = Request("openEnded", new DateOnly(2026, 9, 8));

        var plan = _generator.Generate(request);

        Assert.Contains(plan.Schedules.SelectMany(schedule => schedule.Stays), stay => stay.Mode == "minimumNights" && stay.Nights > 1);
    }

    [Fact]
    public void ExactStays_NeverAbsorbSpareNights()
    {
        var request = Request("openEnded", new DateOnly(2026, 9, 7)) with
        {
            Destinations =
            [
                Destination("a", "AMS", "exactNights", 2),
                Destination("b", "CDG", "minimumNights", 1)
            ]
        };

        var plan = _generator.Generate(request);

        var exactStays = plan.Schedules.SelectMany(schedule => schedule.Stays).Where(stay => stay.DestinationId == "a").ToList();
        Assert.NotEmpty(exactStays);
        Assert.All(exactStays, stay => Assert.Equal(2, stay.Nights));
    }

    [Fact]
    public void ImpossibleDateWindow_FailsBeforeAPlanIsReturned()
    {
        var request = Request("returnToStart", new DateOnly(2026, 9, 2)) with
        {
            Destinations = [Destination("a", "AMS", "minimumNights", 2)]
        };

        var error = Assert.Throws<ItineraryValidationException>(() => _generator.Generate(request));

        Assert.Equal("endDate", error.Field);
        Assert.Contains("at least", error.Message);
    }

    [Fact]
    public void OvernightArrival_UsesTheLocalArrivalCalendarDateForTheStay()
    {
        var arrival = new DateTime(2026, 9, 2, 0, 30, 0);

        Assert.Equal(new DateOnly(2026, 9, 2), OptimizedScheduleGenerator.ArrivalDate(arrival));
        Assert.Equal(new DateOnly(2026, 9, 4), OptimizedScheduleGenerator.StayDepartureDate(arrival, 2));
        Assert.False(OptimizedScheduleGenerator.IsStaySatisfied(arrival, new DateOnly(2026, 9, 3), new("minimumNights", 2)));
        Assert.True(OptimizedScheduleGenerator.IsStaySatisfied(arrival, new DateOnly(2026, 9, 4), new("exactNights", 2)));
    }

    [Fact]
    public void GeneratedSchedules_NeverDepartTwoInterCityLegsOnTheSameDay()
    {
        var plan = _generator.Generate(Request("returnToStart", new DateOnly(2026, 9, 7)));

        Assert.All(plan.Schedules, schedule =>
            Assert.Equal(schedule.Legs.Count, schedule.Legs.Select(leg => leg.DepartureDate).Distinct().Count()));
    }

    [Fact]
    public void ExactZeroNightIntermediateStay_AllowsAnOvernightArrivalWithoutTwoDeparturesOnOneDay()
    {
        var request = Request("returnToStart", new DateOnly(2026, 9, 4)) with
        {
            Destinations = [Destination("a", "AMS", "exactNights", 0)]
        };

        var plan = _generator.Generate(request);

        Assert.Contains(plan.Schedules, schedule =>
            schedule.Stays[0].Nights == 0 &&
            schedule.Stays[0].ArrivalDate == schedule.Stays[0].DepartureDate &&
            schedule.Legs[0].DepartureDate < schedule.Legs[1].DepartureDate);
    }

    [Fact]
    public void MaximumConfiguredOptimizerInput_IsPrunedAtTheConfiguredStateLimit()
    {
        var options = new MultiDestinationSearchOptions();
        var generator = new OptimizedScheduleGenerator(Options.Create(options));
        var airports = new[] { "AAA", "AAB", "AAC", "AAD", "AAE" };
        var request = new OptimizedTripRequest(
            new("start", "Start", [.. airports]),
            Enumerable.Range(0, options.MaxOptimizedDestinations)
                .Select(index => new DestinationRequest(
                    new($"destination-{index}", $"Destination {index}", [.. airports.Select(code => $"{code[0]}{code[2]}{(char)('F' + index)}")]),
                    new("minimumNights", 1)))
                .ToList(),
            "returnToStart",
            null,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 1).AddDays(options.MaxTripDays - 1),
            "sameAirport",
            9,
            "first",
            "recommended");

        var plan = generator.Generate(request);

        Assert.InRange(plan.Schedules.Count, 1, options.MaxActiveStates);
        Assert.True(plan.Feasibility.Bounded);
        Assert.InRange(plan.Feasibility.GeneratedScheduleCount, 1, options.MaxActiveStates);
    }

    private static OptimizedTripRequest Request(string endpointMode, DateOnly endDate) => new(
        Group("start", "DUB"),
        [Destination("a", "AMS", "minimumNights", 1), Destination("b", "CDG", "minimumNights", 1)],
        endpointMode,
        endpointMode == "fixedEnd" ? Group("finish", "LHR") : null,
        new DateOnly(2026, 9, 1),
        endDate,
        "sameAirport",
        1,
        "economy",
        "recommended");

    private static DestinationRequest Destination(string id, string code, string mode, int nights) =>
        new(Group(id, code), new(mode, nights));

    private static AirportGroupRequest Group(string id, string code) => new(id, id, [code]);
}
