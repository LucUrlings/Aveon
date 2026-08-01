using backend.Features.ItinerarySearch.Models;
using backend.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace backend.Features.ItinerarySearch;

[ApiController]
[Route("api/v1/itinerary-searches")]
public sealed class ItinerarySearchController(IItinerarySearchService service, IOptions<MultiDestinationSearchOptions> options) : ControllerBase
{
    [HttpGet("configuration")]
    [ProducesResponseType<ItinerarySearchCapabilitiesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ItinerarySearchCapabilitiesResponse> Configuration()
    {
        if (!options.Value.Enabled) return NotFound();
        var configured = options.Value;
        return Ok(new ItinerarySearchCapabilitiesResponse(
            ResolveProviderCallLimit(),
            configured.MaxOptimizedDestinations,
            configured.MaxAirportsPerGroup,
            configured.MaxTripDays,
            configured.MaxOrderedLegs));
    }

    [HttpPost]
    [ProducesResponseType<ItinerarySearchSessionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItinerarySearchSessionResponse>> Start([FromBody] ItinerarySearchRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return NotFound();
        try { return Accepted(await service.StartAsync(request, ResolveProviderCallLimit(), cancellationToken)); }
        catch (ItineraryValidationException exception)
        {
            ItinerarySearchTelemetry.RecordValidationFailure(request is OptimizedTripRequest ? "optimize" : "ordered");
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { [exception.Field] = [exception.Message] }));
        }
    }
    [HttpGet("{searchId}")]
    [ProducesResponseType<ItinerarySearchSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItinerarySearchSessionResponse>> Get(string searchId, [FromQuery] ItineraryResultsQuery query, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return NotFound();
        return await service.GetAsync(searchId, query, cancellationToken) is { } session ? Ok(session) : NotFound();
    }
    [HttpDelete("{searchId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(string searchId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return NotFound();
        return await service.CancelAsync(searchId, cancellationToken) is null ? NotFound() : NoContent();
    }

    private int ResolveProviderCallLimit()
    {
        var configured = options.Value;
        if (HttpContext?.User?.IsInRole(ApplicationRoles.Admin) == true) return configured.AdminMaxProviderCalls;
        if (HttpContext?.User?.IsInRole(ApplicationRoles.User) == true) return configured.UserMaxProviderCalls;
        return configured.AnonymousMaxProviderCalls;
    }
}
