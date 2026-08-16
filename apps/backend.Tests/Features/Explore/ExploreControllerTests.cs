using backend.Features.Explore;
using backend.Features.Explore.Models;
using backend.Infrastructure.Airports;
using backend.Infrastructure.Auth;
using backend.Infrastructure.Providers.FlightApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using Xunit;

namespace backend.Tests;

public sealed class ExploreControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("Dublin")]
    [InlineData("12A")]
    public async Task Routes_RejectsInvalidIataCodes(string origin)
    {
        var service = new CapturingService();
        var result = await CreateController(service).Routes(origin, CancellationToken.None);

        Assert.IsType<ObjectResult>(result.Result);
        Assert.Null(service.Origin);
    }

    [Fact]
    public async Task Routes_NormalizesOriginAndUsesExploreCacheProfile()
    {
        var service = new CapturingService();
        var result = await CreateController(service).Routes(" dub ", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(("DUB", ExploreCacheProfile.Explore), (service.Origin, service.Profile));
    }

    [Fact]
    public async Task Routes_PassesTheExactDepartureDateToExplore()
    {
        var service = new CapturingService();
        var departureDate = new DateOnly(2026, 9, 18);

        var result = await CreateController(service).Routes("DUB", CancellationToken.None, departureDate);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(departureDate, service.DepartureDate);
    }

    [Fact]
    public async Task Hero_UsesCuratedHeroService()
    {
        var service = new CapturingService();
        var result = await CreateController(service).Hero(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.True(service.HeroCalled);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(367)]
    public async Task Routes_RejectsDepartureDatesOutsideTheSupportedWindow(int offsetDays)
    {
        var service = new CapturingService();

        var result = await CreateController(service).Routes(
            "DUB",
            CancellationToken.None,
            DateOnly.FromDateTime(Now.UtcDateTime).AddDays(offsetDays));

        var invalid = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(invalid.Value);
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, "departureDate", StringComparison.OrdinalIgnoreCase));
        Assert.Null(service.Origin);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Routes_AcceptsDepartureDatesAtTheSupportedWindowBoundaries(int offsetDays)
    {
        var service = new CapturingService();
        var departureDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(offsetDays);

        var result = await CreateController(service).Routes("DUB", CancellationToken.None, departureDate);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(departureDate, service.DepartureDate);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Routes_PreservesAndSerializesCompletenessAndStalenessMetadata(bool isComplete, bool isStale)
    {
        var service = new CapturingService(Response(isComplete, isStale));
        var result = await CreateController(service).Routes("DUB", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ExploreRoutesResponse>(ok.Value);
        Assert.Equal(isComplete, response.IsComplete);
        Assert.Equal(isStale, response.IsStale);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains($"\"isComplete\":{isComplete.ToString().ToLowerInvariant()}", json);
        Assert.Contains($"\"isStale\":{isStale.ToString().ToLowerInvariant()}", json);
        Assert.Contains("\"observedFrom\":\"2026-07-28\"", json);
        Assert.Contains("\"fetchedAt\":\"2026-08-02T12:00:00+00:00\"", json);
    }

    [Fact]
    public async Task Hero_ReturnsTheSamePublicRouteContract()
    {
        var service = new CapturingService(Response(true, true));
        var result = await CreateController(service).Hero(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ExploreRoutesResponse>(ok.Value);
        Assert.True(response.IsComplete);
        Assert.True(response.IsStale);
        Assert.Equal("DUB", response.Origin.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderFailure_ReturnsSafeBadGatewayProblem(bool hero)
    {
        var service = new FailingService();
        var controller = CreateController(service);

        var result = hero
            ? await controller.Hero(CancellationToken.None)
            : await controller.Routes("DXB", CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(failed.Value);
        Assert.Equal("Flight schedule provider unavailable", problem.Title);
        Assert.DoesNotContain("provider-secret", problem.Detail);
    }

    [Fact]
    public async Task WrappedTransportFailure_ReturnsSafeBadGatewayProblem()
    {
        var controller = CreateController(new WrappedTransportFailingService());

        var result = await controller.Routes("DUB", CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(failed.Value);
        Assert.Equal("Flight schedule provider unavailable", problem.Title);
        Assert.DoesNotContain("transport-secret", problem.Detail);
    }

    [Fact]
    public async Task MissingAirportCatalogue_ReturnsSafeServiceUnavailableProblem()
    {
        var controller = CreateController(new MissingCatalogService());

        var result = await controller.Routes("DUB", CancellationToken.None);

        var failed = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, failed.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(failed.Value);
        Assert.Equal("Airport catalogue unavailable", problem.Title);
        Assert.DoesNotContain("DUB", problem.Detail);
    }

    [Fact]
    public async Task CatalogRefresh_RequiresAdminAndInvokesTheForcedSharedRefreshPath()
    {
        var authorization = Assert.Single(typeof(AirportCatalogController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());
        Assert.Equal(ApplicationRoles.Admin, authorization.Roles);
        var refreshService = new CapturingCatalogRefreshService();

        var result = await new AirportCatalogController(refreshService).Refresh(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(AirportCatalogRefreshStatus.Refreshed, Assert.IsType<AirportCatalogRefreshResult>(ok.Value).Status);
        Assert.True(refreshService.Force);
    }

    private static ExploreRoutesResponse Response(bool isComplete = true, bool isStale = false) => new(
        new("DUB", "Dublin Airport", "Dublin", "Ireland", 53.42, -6.27),
        [], new(2026, 7, 28), new(2026, 8, 7), new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), isComplete, isStale);

    private static ExploreController CreateController(IExploreRouteService service) =>
        new(service, new FixedTimeProvider(Now));

    private sealed class CapturingService(ExploreRoutesResponse? response = null) : IExploreRouteService
    {
        private readonly ExploreRoutesResponse _response = response ?? Response();
        public string? Origin { get; private set; }
        public ExploreCacheProfile Profile { get; private set; }
        public bool HeroCalled { get; private set; }

        public DateOnly? DepartureDate { get; private set; }

        public Task<ExploreRoutesResponse> GetRoutesAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null)
        {
            Origin = origin;
            Profile = profile;
            DepartureDate = departureDate;
            return Task.FromResult(_response);
        }

        public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken)
        {
            HeroCalled = true;
            return Task.FromResult(_response);
        }
    }

    private sealed class FailingService : IExploreRouteService
    {
        private static readonly FlightApiResponseException Failure = new(
            HttpStatusCode.BadRequest,
            "Something went wrong, please try again provider-secret",
            "provider-request-id");

        public Task<ExploreRoutesResponse> GetRoutesAsync(
            string origin,
            ExploreCacheProfile profile,
            CancellationToken cancellationToken,
            DateOnly? departureDate = null) => Task.FromException<ExploreRoutesResponse>(Failure);

        public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken) =>
            Task.FromException<ExploreRoutesResponse>(Failure);
    }

    private sealed class MissingCatalogService : IExploreRouteService
    {
        public Task<ExploreRoutesResponse> GetRoutesAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null) =>
            Task.FromException<ExploreRoutesResponse>(new AirportCatalogUnavailableException("missing DUB"));

        public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken) =>
            Task.FromException<ExploreRoutesResponse>(new AirportCatalogUnavailableException("missing DUB"));
    }

    private sealed class WrappedTransportFailingService : IExploreRouteService
    {
        private static readonly ExploreProviderUnavailableException Failure = new(
            "safe wrapper",
            new HttpRequestException("transport-secret"));

        public Task<ExploreRoutesResponse> GetRoutesAsync(string origin, ExploreCacheProfile profile, CancellationToken cancellationToken, DateOnly? departureDate = null) =>
            Task.FromException<ExploreRoutesResponse>(Failure);

        public Task<ExploreRoutesResponse> GetHeroRoutesAsync(CancellationToken cancellationToken) =>
            Task.FromException<ExploreRoutesResponse>(Failure);
    }

    private sealed class CapturingCatalogRefreshService : IAirportCatalogRefreshService
    {
        public bool Force { get; private set; }

        public Task<AirportCatalogRefreshResult> RefreshAsync(bool force, CancellationToken cancellationToken)
        {
            Force = force;
            return Task.FromResult(new AirportCatalogRefreshResult(AirportCatalogRefreshStatus.Refreshed, 7_884, 0));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
