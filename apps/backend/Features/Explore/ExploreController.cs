using backend.Features.Explore.Models;
using backend.Infrastructure.Providers.FlightApi;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace backend.Features.Explore;

[ApiController]
[Route("api/v1/explore")]
public sealed partial class ExploreController(
    IExploreRouteService service,
    TimeProvider timeProvider) : ControllerBase
{
    internal const int MaximumDepartureDateAdvanceDays = 365;
    private const int GlobalDateBoundaryToleranceDays = 1;

    [HttpGet("routes")]
    [ProducesResponseType<ExploreRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ExploreRoutesResponse>> Routes(
        [FromQuery] string origin = "",
        CancellationToken cancellationToken = default,
        [FromQuery] DateOnly? departureDate = null)
    {
        var normalized = origin.Trim().ToUpperInvariant();
        if (!IataCode().IsMatch(normalized))
        {
            ModelState.AddModelError(nameof(origin), "Origin must be a three-letter IATA airport code.");
            return ValidationProblem(ModelState);
        }

        if (departureDate is { } exactDate)
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var earliestSupportedDate = today.AddDays(-GlobalDateBoundaryToleranceDays);
            var latestSupportedDate = today.AddDays(MaximumDepartureDateAdvanceDays + GlobalDateBoundaryToleranceDays);
            if (exactDate < earliestSupportedDate || exactDate > latestSupportedDate)
            {
                ModelState.AddModelError(
                    nameof(departureDate),
                    "Departure date must fall within the supported 365-day travel-planning window.");
                return ValidationProblem(ModelState);
            }
        }

        try
        {
            return Ok(await service.GetRoutesAsync(normalized, ExploreCacheProfile.Explore, cancellationToken, departureDate));
        }
        catch (Exception exception) when (exception is FlightApiResponseException or ExploreProviderUnavailableException)
        {
            return ProviderUnavailable();
        }
        catch (AirportCatalogUnavailableException)
        {
            return CatalogUnavailable();
        }
    }

    [HttpGet("hero")]
    [ProducesResponseType<ExploreRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ExploreRoutesResponse>> Hero(CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.GetHeroRoutesAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is FlightApiResponseException or ExploreProviderUnavailableException)
        {
            return ProviderUnavailable();
        }
        catch (AirportCatalogUnavailableException)
        {
            return CatalogUnavailable();
        }
    }

    private static ObjectResult ProviderUnavailable() => new(new ProblemDetails
    {
        Status = StatusCodes.Status502BadGateway,
        Title = "Flight schedule provider unavailable",
        Detail = "FlightAPI could not provide the airport schedule. Please try again shortly."
    })
    {
        StatusCode = StatusCodes.Status502BadGateway
    };

    private static ObjectResult CatalogUnavailable() => new(new ProblemDetails
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title = "Airport catalogue unavailable",
        Detail = "Airport location data is still being prepared. Please try again shortly."
    })
    {
        StatusCode = StatusCodes.Status503ServiceUnavailable
    };

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex IataCode();
}
