using System.Security.Claims;
using backend.Features.Search.Models;
using backend.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace backend.Features.Search;

public sealed class SearchLimitResolver(IOptions<SearchOptions> searchOptions) : ISearchLimitResolver
{
    public SearchLimit Resolve(ClaimsPrincipal user)
    {
        if (user.IsInRole(ApplicationRoles.Admin))
        {
            return new SearchLimit(null, null);
        }

        if (user.IsInRole(ApplicationRoles.User))
        {
            var userLimit = Math.Max(searchOptions.Value.UserMaxSearchCombinations, 1);
            return new SearchLimit(
                userLimit,
                $"Search exceeds the limit of {userLimit} combinations.");
        }

        var anonymousLimit = Math.Max(searchOptions.Value.AnonymousMaxSearchCombinations, 1);
        var signedUpLimit = Math.Max(searchOptions.Value.UserMaxSearchCombinations, 1);
        return new SearchLimit(
            anonymousLimit,
            $"Search exceeds the guest limit of {anonymousLimit} combinations. Sign up or log in to search up to {signedUpLimit} combinations.");
    }
}
