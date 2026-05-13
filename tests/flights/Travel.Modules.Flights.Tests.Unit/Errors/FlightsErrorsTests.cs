using ErrorOr;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;

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
        };

        // Act & Assert
        foreach (var error in errors)
        {
            Assert.StartsWith("Flights.", error.Code);
        }
    }
}
