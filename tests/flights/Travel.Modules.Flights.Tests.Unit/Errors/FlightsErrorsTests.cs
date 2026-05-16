using ErrorOr;
using Shouldly;
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

    // ── ErrorType assertions for each FlightsErrors member ───────────────────

    [Fact]
    public void OfferExpired_has_Validation_type()
    {
        FlightsErrors.OfferExpired.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void OfferNotFound_has_NotFound_type()
    {
        FlightsErrors.OfferNotFound("off_123").Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void PriceChanged_has_Conflict_type()
    {
        var usd = CurrencyCode.Create("USD").Value;
        FlightsErrors
            .PriceChanged(Money.Create(100m, usd).Value, Money.Create(120m, usd).Value)
            .Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void ProviderUnavailable_has_Failure_type()
    {
        FlightsErrors.ProviderUnavailable("Duffel").Type.ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public void ProviderRateLimited_has_Failure_type()
    {
        FlightsErrors.ProviderRateLimited("Duffel").Type.ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public void PaymentFailed_has_Failure_type()
    {
        FlightsErrors.PaymentFailed("declined").Type.ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public void PassengerInvalid_has_Validation_type()
    {
        FlightsErrors.PassengerInvalid("bad name").Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void OrderNotCancellable_has_Conflict_type()
    {
        FlightsErrors.OrderNotCancellable("reason").Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void IdempotencyConflict_has_Conflict_type()
    {
        FlightsErrors.IdempotencyConflict.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void ConcurrencyConflict_has_Conflict_type()
    {
        FlightsErrors.ConcurrencyConflict.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void NlSearchUnparseable_has_Validation_type()
    {
        FlightsErrors.NlSearchUnparseable.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void NlSearchDisabled_has_custom_numeric_type_503()
    {
        // NlSearchDisabled uses Error.Custom(503, ...) — NumericType carries the HTTP status.
        // ErrorType enum has no named "Custom" entry in ErrorOr v2; the value falls outside
        // the named range.
        FlightsErrors.NlSearchDisabled.NumericType.ShouldBe(503);
    }
}
