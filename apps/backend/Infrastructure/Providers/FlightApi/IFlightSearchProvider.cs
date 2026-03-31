using backend.Infrastructure.Models;
using backend.Infrastructure.Providers.FlightApi.Models;

namespace backend.Infrastructure.Providers.FlightApi;

public interface IFlightSearchProvider
{
    Task<FlightApiOneWayResponse> SearchOneWayAsync(
        ProviderSearchRequest request,
        CancellationToken cancellationToken);

    Task<FlightApiOneWayResponse> SearchRoundTripAsync(
        ProviderRoundTripSearchRequest request,
        CancellationToken cancellationToken);
}
