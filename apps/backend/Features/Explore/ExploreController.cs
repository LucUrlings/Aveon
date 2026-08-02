using backend.Features.Explore.Models;
using backend.Infrastructure.Providers.FlightApi;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace backend.Features.Explore;

[ApiController]
[Route("api/v1/explore")]
public sealed partial class ExploreController(IExploreRouteService service) : ControllerBase
{
    [HttpGet("routes")]
    [ProducesResponseType<ExploreRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ExploreRoutesResponse>> Routes(
        [FromQuery] string origin = "",
        CancellationToken cancellationToken = default)
    {
        var normalized = origin.Trim().ToUpperInvariant();
        if (!IataCode().IsMatch(normalized))
        {
            ModelState.AddModelError(nameof(origin), "Origin must be a three-letter IATA airport code.");
            return ValidationProblem(ModelState);
        }

        try
        {
            return Ok(await service.GetRoutesAsync(normalized, ExploreCacheProfile.Explore, cancellationToken));
        }
        catch (FlightApiResponseException)
        {
            return ProviderUnavailable();
        }
    }

    [HttpGet("hero")]
    [ProducesResponseType<ExploreRoutesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ExploreRoutesResponse>> Hero(CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await service.GetHeroRoutesAsync(cancellationToken));
        }
        catch (FlightApiResponseException)
        {
            return ProviderUnavailable();
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

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex IataCode();
}
