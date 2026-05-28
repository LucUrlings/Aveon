using System.Security.Claims;

namespace backend.Features.Search;

public interface ISearchLimitResolver
{
    SearchLimit Resolve(ClaimsPrincipal user);
}

public sealed record SearchLimit(int? MaxSearchCombinations, string? ExceededMessage);
