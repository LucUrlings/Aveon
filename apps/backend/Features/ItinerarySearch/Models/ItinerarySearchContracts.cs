using System.Text.Json.Serialization;

namespace backend.Features.ItinerarySearch.Models;

public record AirportGroupRequest(string Id, string Label, List<string> AirportCodes);
public record StayRuleRequest(string Mode, int Nights);
public record DestinationRequest(AirportGroupRequest Group, StayRuleRequest Stay, string AirportContinuity = "inherit");
public record OrderedLegRequest(string Id, AirportGroupRequest From, AirportGroupRequest To, DateOnly DepartureDate, string AirportContinuityWithPrevious = "sameAirport");

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(OptimizedTripRequest), "optimize")]
[JsonDerivedType(typeof(OrderedTripRequest), "ordered")]
public abstract record ItinerarySearchRequest(int Adults, string CabinClass, string Ranking);

public sealed record OptimizedTripRequest(
    AirportGroupRequest Start,
    List<DestinationRequest> Destinations,
    string EndpointMode,
    AirportGroupRequest? FixedEnd,
    DateOnly StartDate,
    DateOnly EndDate,
    string DefaultAirportContinuity,
    int Adults,
    string CabinClass,
    string Ranking) : ItinerarySearchRequest(Adults, CabinClass, Ranking);

public sealed record OrderedTripRequest(
    List<OrderedLegRequest> Legs,
    int Adults,
    string CabinClass,
    string Ranking) : ItinerarySearchRequest(Adults, CabinClass, Ranking);

public record SearchCoverage(string Mode = "exhaustive", int LiveProviderCallsUsed = 0, int ProviderCallLimit = 0, int CacheHits = 0, int CandidateStatesEvaluated = 0, int CandidateStatesPruned = 0);
public record OptimizerFeasibility(
    int RequiredLegCount,
    int MinimumCalendarDays,
    int AvailableCalendarDays,
    int RouteOrderCount,
    int GeneratedScheduleCount,
    bool Bounded);
public record AbstractScheduleLeg(string Id, string FromGroupId, string ToGroupId, DateOnly DepartureDate, DateOnly? RequiredArrivalDate = null);
public record AbstractScheduleStay(string DestinationId, DateOnly ArrivalDate, DateOnly DepartureDate, int Nights, string Mode, int RequiredNights);
public record AbstractItinerarySchedule(
    string Id,
    List<string> DestinationOrder,
    List<AbstractScheduleLeg> Legs,
    List<AbstractScheduleStay> Stays);
public record ItineraryWarning(string Code, string Message);
public record RankingBreakdown(decimal Score, decimal TotalPrice, int AdditionalFlightMinutes, int TotalStops, int AdditionalBookings, int AirportSwitches);
public record ItinerarySegment(string MarketingCarrierName, string MarketingCarrierCode, string FlightNumber, string OriginAirport, string DestinationAirport, DateTime DepartureLocalTime, DateTime ArrivalLocalTime, int DurationMinutes);
public record ItineraryLeg(string Id, string OriginAirport, string DestinationAirport, DateTime DepartureLocalTime, DateTime ArrivalLocalTime, int DurationMinutes, int Stops, List<ItinerarySegment>? Segments = null);
public record ItineraryStay(string DestinationId, DateOnly ArrivalDate, DateOnly DepartureDate, int Nights);
public record BookingOption(string Label, string Url, decimal Price, string Currency, string Provider);
public record ItineraryResult(
    string Id,
    string BookingType,
    List<string> DestinationOrder,
    List<ItineraryLeg> Legs,
    List<ItineraryStay> Stays,
    decimal TotalPrice,
    string Currency,
    int TotalFlightDurationMinutes,
    int TotalStops,
    int BookingCount,
    int AirportSwitches,
    List<BookingOption> BookingOptions,
    List<ItineraryWarning> Warnings,
    RankingBreakdown RankingBreakdown);
public record ItineraryPagination(int Page, int PageSize, int TotalResults, int TotalPages);
public record ItineraryFilterOption(string Value, string Label, int Count);
public record ItineraryFilterMetadata(
    List<ItineraryFilterOption> Airlines,
    List<ItineraryFilterOption> BookingSources,
    List<ItineraryFilterOption> DepartureAirports,
    List<ItineraryFilterOption> ArrivalAirports,
    decimal? MinPrice,
    decimal? MaxPrice,
    int? MaxDurationMinutes,
    int? MaxBookingCount,
    int? MaxAirportSwitches);
public record ItinerarySearchSessionResponse(
    string SearchId,
    string Mode,
    string Status,
    string Phase,
    int Progress,
    SearchCoverage Coverage,
    List<ItineraryResult> Results,
    List<ItineraryWarning> Warnings,
    string? ErrorMessage = null,
    ItineraryPagination? Pagination = null,
    ItineraryFilterMetadata? Filters = null,
    OptimizerFeasibility? Feasibility = null,
    List<AbstractItinerarySchedule>? AbstractSchedules = null);
public record ItinerarySearchCapabilitiesResponse(
    int ProviderCallLimit,
    int MaxOptimizedDestinations,
    int MaxAirportsPerGroup,
    int MaxTripDays,
    int MaxOrderedLegs);

public record ItineraryResultsQuery
{
    public string? Ranking { get; init; }
    public bool? Direct { get; init; }
    public bool? OneStop { get; init; }
    public bool? TwoPlusStops { get; init; }
    public string? Airlines { get; init; }
    public string? BookingSources { get; init; }
    public string? DepartureAirports { get; init; }
    public string? ArrivalAirports { get; init; }
    public decimal? MaxPrice { get; init; }
    public int? MaxDurationMinutes { get; init; }
    public string? DepartureTime { get; init; }
    public string? ArrivalTime { get; init; }
    public string? BookingType { get; init; }
    public int? MaxBookingCount { get; init; }
    public bool? AllowAirportSwitches { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
