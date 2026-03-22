using backend.Features.Airports.Models;
using Microsoft.AspNetCore.Mvc;

namespace backend.Features.Airports;

[ApiController]
[Route("api/v1/[controller]")]
public class AirportsController(IAirportService airportService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AirportLookupResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AirportLookupResponse>> Search(
        [FromQuery] string query = "",
        CancellationToken cancellationToken = default)
    {
        var response = await airportService.SearchAsync(query, cancellationToken);
        return Ok(response);
    }
}
