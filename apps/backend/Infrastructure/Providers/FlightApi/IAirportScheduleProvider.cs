using backend.Infrastructure.Providers.FlightApi.Models;

namespace backend.Infrastructure.Providers.FlightApi;

public interface IAirportScheduleProvider
{
    Task<FlightApiScheduleResponse> SearchDepartureScheduleAsync(string originAirport, int page, CancellationToken cancellationToken);
}
