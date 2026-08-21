using System.Security.Claims;
using backend.Features.Search;
using backend.Features.Search.Models;
using backend.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace backend.Tests;

public sealed class SearchLimitResolverTests
{
    [Fact]
    public void Resolve_ReturnsAnonymousLimitAndSignupMessage_WhenUserIsNotAuthenticated()
    {
        var resolver = CreateResolver();

        var limit = resolver.Resolve(new ClaimsPrincipal());

        Assert.Equal(75, limit.MaxSearchCombinations);
        Assert.Equal("Search exceeds the guest limit of 75 combinations. Sign up or log in to search up to 200 combinations.", limit.ExceededMessage);
    }

    [Fact]
    public void Resolve_ReturnsUserLimit_WhenUserHasUserRole()
    {
        var resolver = CreateResolver();

        var limit = resolver.Resolve(CreatePrincipal(ApplicationRoles.User));

        Assert.Equal(200, limit.MaxSearchCombinations);
        Assert.Equal("Search exceeds the limit of 200 combinations.", limit.ExceededMessage);
    }

    [Fact]
    public void Resolve_ReturnsNoLimit_WhenUserHasAdminRole()
    {
        var resolver = CreateResolver();

        var limit = resolver.Resolve(CreatePrincipal(ApplicationRoles.User, ApplicationRoles.Admin));

        Assert.Null(limit.MaxSearchCombinations);
        Assert.Null(limit.ExceededMessage);
    }

    private static SearchLimitResolver CreateResolver() =>
        new(Options.Create(new SearchOptions
        {
            AnonymousMaxSearchCombinations = 75,
            UserMaxSearchCombinations = 200,
        }));

    private static ClaimsPrincipal CreatePrincipal(params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
