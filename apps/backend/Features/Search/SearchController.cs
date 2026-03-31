using backend.Features.Search.Models;
using Microsoft.AspNetCore.Mvc;

namespace backend.Features.Search;

[ApiController]
[Route("api/v1/[controller]")]
public class SearchController(ISearchService searchService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SearchSessionResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchSessionResponse>> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchService.StartSearchAsync(request, cancellationToken);
            return Accepted(response);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(SearchRequest), ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    [HttpGet("{searchId}")]
    [ProducesResponseType<SearchSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchSessionResponse>> Get(
        [FromRoute] string searchId,
        [FromQuery] SearchResultsQuery query,
        CancellationToken cancellationToken)
    {
        var response = await searchService.GetSearchAsync(searchId, query, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }
}
