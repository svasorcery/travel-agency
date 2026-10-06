using ErrorOr;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightCancellationProvider
{
    Task<ErrorOr<CancellationEligibility>> InspectOrderAsync(string orderRef, CancellationToken ct);
    Task<ErrorOr<CancellationQuoteResult>> CreateTermsAsync(string orderRef, CancellationToken ct);
    Task<ErrorOr<CancellationEffectResult>> ConfirmAsync(
        CancellationTerms terms,
        CancellationToken ct
    );
    Task<ErrorOr<CancellationObservation>> ObserveAsync(
        CancellationCorrelation correlation,
        CancellationToken ct
    );
}
