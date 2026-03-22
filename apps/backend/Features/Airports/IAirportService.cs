using backend.Features.Airports.Models;

namespace backend.Features.Airports;

public interface IAirportService
{
    Task<AirportLookupResponse> SearchAsync(string query, CancellationToken cancellationToken);
}
