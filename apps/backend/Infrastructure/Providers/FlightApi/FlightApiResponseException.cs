using System.Net;

namespace backend.Infrastructure.Providers.FlightApi;

public sealed class FlightApiResponseException(
    HttpStatusCode statusCode,
    string responseSummary,
    string? providerRequestId)
    : HttpRequestException(BuildMessage(statusCode, responseSummary, providerRequestId), null, statusCode)
{
    public string ResponseSummary { get; } = responseSummary;
    public string? ProviderRequestId { get; } = providerRequestId;

    private static string BuildMessage(HttpStatusCode statusCode, string responseSummary, string? providerRequestId)
    {
        var requestId = string.IsNullOrWhiteSpace(providerRequestId) ? string.Empty : $" Provider request ID: {providerRequestId}.";
        return $"FlightApi returned HTTP {(int)statusCode} ({statusCode}). Provider response: {responseSummary}.{requestId}";
    }
}
