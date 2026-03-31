using backend.Features.Search;
using backend.Features.Search.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace backend.Tests;

public sealed class SearchControllerTests
{
    [Fact]
    public async Task Search_ReturnsAccepted_WhenServiceSucceeds()
    {
        var session = CreateSession(status: "running");
        var controller = new SearchController(new StubSearchService(startResponse: session));

        var result = await controller.Search(CreateRequest(), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        Assert.Same(session, accepted.Value);
    }

    [Fact]
    public async Task Search_ReturnsValidationProblem_WhenServiceThrowsArgumentException()
    {
        var controller = new SearchController(new StubSearchService(startException: new ArgumentException("Bad request")));

        var result = await controller.Search(CreateRequest(), CancellationToken.None);

        var validation = Assert.IsType<ObjectResult>(result.Result);
        var details = Assert.IsType<ValidationProblemDetails>(validation.Value);
        Assert.Contains("Bad request", details.Errors[nameof(SearchRequest)]);
    }

    [Fact]
    public async Task Get_ReturnsOk_WhenSessionExists()
    {
        var session = CreateSession(status: "completed");
        var controller = new SearchController(new StubSearchService(getResponse: session));

        var result = await controller.Get("search-1", new SearchResultsQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(session, ok.Value);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenSessionMissing()
    {
        var controller = new SearchController(new StubSearchService(getResponse: null));

        var result = await controller.Get("missing", new SearchResultsQuery(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    private static SearchRequest CreateRequest() =>
        new(["DUB"], ["AMS"], [new DateOnly(2026, 5, 15)], null, null, 1, "economy");

    private static SearchSessionResponse CreateSession(string status) =>
        new(
            "search-1",
            status,
            1,
            status == "running" ? 0 : 1,
            0,
            new SearchResponse(
                [],
                new SearchMetadata(1, 0, 0, 0, 0, 0),
                new SearchFiltersMetadata([], [], [], [], new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchRangeMetadata(0, 0), new SearchStopFilterMetadata(0, 0, 0)),
                new SearchPagination(1, 0, 0, 0)),
            null);

    private sealed class StubSearchService(
        SearchSessionResponse? startResponse = null,
        SearchSessionResponse? getResponse = null,
        Exception? startException = null) : ISearchService
    {
        public Task<SearchSessionResponse> StartSearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            if (startException is not null)
            {
                throw startException;
            }

            return Task.FromResult(startResponse ?? throw new InvalidOperationException("Missing start response."));
        }

        public Task<SearchSessionResponse?> GetSearchAsync(string searchId, SearchResultsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(getResponse);
    }
}
