namespace backend.Features.Search.Models;

public record SearchResultBookingLink(
    string Label,
    string Url,
    SearchResultPrice? Price = null
);
