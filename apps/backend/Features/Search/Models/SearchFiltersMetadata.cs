namespace backend.Features.Search.Models;

public record SearchFiltersMetadata(
    List<SearchFilterOptionCount> Providers,
    List<SearchFilterOptionCount> Airlines,
    List<SearchFilterOptionCount> DepartureAirports,
    List<SearchFilterOptionCount> ArrivalAirports,
    SearchRangeMetadata DurationMinutes,
    SearchRangeMetadata DepartureTimeMinutes,
    SearchRangeMetadata ArrivalTimeMinutes,
    SearchStopFilterMetadata Stops
);
