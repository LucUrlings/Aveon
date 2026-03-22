namespace backend.Features.Search.Models;

public record SearchResultPriceOption(
    string Id,
    string Provider,
    SearchResultPrice TotalPrice,
    string DeepLink
);
