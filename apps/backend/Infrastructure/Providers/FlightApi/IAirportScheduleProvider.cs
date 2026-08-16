using backend.Infrastructure.Providers.FlightApi.Models;

namespace backend.Infrastructure.Providers.FlightApi;

public interface IAirportScheduleProvider
{
    Task<FlightApiScheduleResponse> SearchDepartureScheduleAsync(string originAirport, int page, CancellationToken cancellationToken);

    Task<FlightApiScheduleV2Response> SearchDepartureScheduleV2Async(string originAirport, DateOnly departureDate, int page, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The provider does not support exact-date airport schedules.");
}
