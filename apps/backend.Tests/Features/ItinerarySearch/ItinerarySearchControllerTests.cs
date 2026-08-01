using backend.Features.ItinerarySearch;
using backend.Features.ItinerarySearch.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Xunit;

namespace backend.Tests;

public sealed class ItinerarySearchControllerTests
{
    [Fact]
    public async Task DisabledFeature_ReturnsNotFound()
    {
        var controller = CreateController(enabled: false);
        var result = await controller.Start(ItinerarySearchServiceTests.CreateOrderedRequest(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task ValidationFailure_ReturnsFieldSpecificValidationProblem()
    {
        var controller = new ItinerarySearchController(new ThrowingService(), Options.Create(new MultiDestinationSearchOptions { Enabled = true }));
        var result = await controller.Start(ItinerarySearchServiceTests.CreateOrderedRequest(), CancellationToken.None);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Equal("Must change.", Assert.Single(details.Errors["legs"]));
    }

    [Fact]
    public async Task StartGetAndDelete_ExposeStableSessionLifecycle()
    {
        var controller = CreateController(enabled: true);
        var started = Assert.IsType<AcceptedResult>((await controller.Start(ItinerarySearchServiceTests.CreateOptimizedRequest(), CancellationToken.None)).Result);
        var session = Assert.IsType<ItinerarySearchSessionResponse>(started.Value);
        Assert.IsType<OkObjectResult>((await controller.Get(session.SearchId, new ItineraryResultsQuery(), CancellationToken.None)).Result);
        Assert.IsType<NoContentResult>(await controller.Cancel(session.SearchId, CancellationToken.None));
        var canceled = Assert.IsType<OkObjectResult>((await controller.Get(session.SearchId, new ItineraryResultsQuery(), CancellationToken.None)).Result);
        Assert.Equal("canceled", Assert.IsType<ItinerarySearchSessionResponse>(canceled.Value).Status);
    }

    [Fact]
    public async Task Start_UsesTheAuthenticatedRoleProviderBudget()
    {
        var service = new CapturingService();
        var controller = new ItinerarySearchController(service, Options.Create(new MultiDestinationSearchOptions { Enabled = true, AdminMaxProviderCalls = 222 }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test")) } }
        };

        await controller.Start(ItinerarySearchServiceTests.CreateOrderedRequest(), CancellationToken.None);

        Assert.Equal(222, service.ProviderCallLimit);
    }

    private static ItinerarySearchController CreateController(bool enabled)
    {
        var service = new ItinerarySearchService(new ItinerarySearchServiceTests.MemoryStore(), new ItinerarySearchServiceTests.NoOpRunner(), Options.Create(new MultiDestinationSearchOptions()));
        return new(service, Options.Create(new MultiDestinationSearchOptions { Enabled = enabled }));
    }

    private sealed class ThrowingService : IItinerarySearchService
    {
        public Task<ItinerarySearchSessionResponse> StartAsync(ItinerarySearchRequest request, int providerCallLimit, CancellationToken cancellationToken) => throw new ItineraryValidationException("legs", "Must change.");
        public Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, ItineraryResultsQuery query, CancellationToken cancellationToken) => Task.FromResult<ItinerarySearchSessionResponse?>(null);
        public Task<ItinerarySearchSessionResponse?> CancelAsync(string searchId, CancellationToken cancellationToken) => Task.FromResult<ItinerarySearchSessionResponse?>(null);
    }

    private sealed class CapturingService : IItinerarySearchService
    {
        public int ProviderCallLimit { get; private set; }
        public Task<ItinerarySearchSessionResponse> StartAsync(ItinerarySearchRequest request, int providerCallLimit, CancellationToken cancellationToken)
        {
            ProviderCallLimit = providerCallLimit;
            return Task.FromResult(new ItinerarySearchSessionResponse("id", "ordered", "running", "validating", 0, new(), [], []));
        }
        public Task<ItinerarySearchSessionResponse?> GetAsync(string searchId, ItineraryResultsQuery query, CancellationToken cancellationToken) => Task.FromResult<ItinerarySearchSessionResponse?>(null);
        public Task<ItinerarySearchSessionResponse?> CancelAsync(string searchId, CancellationToken cancellationToken) => Task.FromResult<ItinerarySearchSessionResponse?>(null);
    }
}
