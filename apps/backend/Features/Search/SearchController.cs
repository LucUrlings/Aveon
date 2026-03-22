using backend.Features.Search.Models;
using Microsoft.AspNetCore.Mvc;

namespace backend.Features.Search;

[ApiController]
[Route("api/v1/[controller]")]
public class SearchController(ISearchService searchService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromBody] SearchRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchService.SearchAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(SearchRequest), ex.Message);
            return ValidationProblem(ModelState);
        }
    }
}
