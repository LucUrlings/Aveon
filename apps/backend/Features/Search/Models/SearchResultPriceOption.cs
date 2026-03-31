namespace backend.Features.Search.Models;

public record SearchResultPriceOption(
    string Id,
    string Provider,
    SearchResultPrice TotalPrice,
    List<SearchResultBookingLink> BookingLinks
);
