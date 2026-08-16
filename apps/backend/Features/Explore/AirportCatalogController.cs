using backend.Infrastructure.Airports;
using backend.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Features.Explore;

[ApiController]
[Route("api/v1/explore/catalog")]
[Authorize(Roles = ApplicationRoles.Admin)]
public sealed class AirportCatalogController(IAirportCatalogRefreshService refreshService) : ControllerBase
{
    [HttpPost("refresh")]
    [ProducesResponseType<AirportCatalogRefreshResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AirportCatalogRefreshResult>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await refreshService.RefreshAsync(force: true, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Airport catalogue refresh failed",
                detail: "The previous airport catalogue remains active. Check backend logs for details.");
        }
    }
}
