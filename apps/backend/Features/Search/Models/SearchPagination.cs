namespace backend.Features.Search.Models;

public record SearchPagination(
    int Page,
    int PageSize,
    int TotalResults,
    int TotalPages
);
