using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Search;

public interface IFxRates
{
    Task<ErrorOr<decimal>> GetRateAsync(CurrencyCode from, CurrencyCode to, CancellationToken ct);
    Task<ErrorOr<Money>> ConvertAsync(Money amount, CurrencyCode to, CancellationToken ct);
}
