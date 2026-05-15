using ErrorOr;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Tests.Unit.Errors;

public class FlightsErrorsTests
{
    [Fact]
    public void AllFlightsErrors_HaveCorrectCodePrefix()
    {
        // Arrange
        var usd = CurrencyCode.Create("USD").Value;
        var money1 = Money.Create(100m, usd).Value;
        var money2 = Money.Create(150m, usd).Value;

        var errors = new List<Error>
        {
            FlightsErrors.OfferExpired,
            FlightsErrors.OfferNotFound("test"),
            FlightsErrors.PriceChanged(money1, money2),
            FlightsErrors.ProviderUnavailable("Duffel"),
            FlightsErrors.ProviderRateLimited("Duffel"),
            FlightsErrors.PaymentFailed("Card declined"),
            FlightsErrors.PassengerInvalid("Invalid name"),
            FlightsErrors.OrderNotCancellable("Already confirmed"),
            FlightsErrors.IdempotencyConflict,
            FlightsErrors.NlSearchUnparseable,
            FlightsErrors.NlSearchDisabled,
        };

        // Act & Assert
        foreach (var error in errors)
        {
            Assert.StartsWith("Flights.", error.Code);
        }
    }

    [Fact]
    public void NlSearchDisabled_maps_to_HTTP_503()
    {
        // NlSearchDisabled is an administratively-disabled feature, not a server crash —
        // it must produce 503 Service Unavailable so clients can distinguish it from 500.
        var problem = new List<Error> { FlightsErrors.NlSearchDisabled }.ToProblemDetails();

        Assert.Equal(503, problem.Status);
        Assert.Equal("Flights.NlSearchDisabled", problem.Type?.Split('/').Last());
    }
}
