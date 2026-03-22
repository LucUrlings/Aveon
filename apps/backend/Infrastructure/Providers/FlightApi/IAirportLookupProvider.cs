using backend.Infrastructure.Providers.FlightApi.Models;

namespace backend.Infrastructure.Providers.FlightApi;

public interface IAirportLookupProvider
{
    Task<FlightApiCodeLookupResponse> SearchAirportsAsync(
        string query,
        CancellationToken cancellationToken);
}
