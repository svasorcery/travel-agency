using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class BookingPassengerRawTests
{
    [Fact]
    public void Valid_raw_names_within_bounds_use_existing_canonicalization()
    {
        var result = BookingPassengerDetails.CreateRaw(
            BookingPassengerId.Create(Guid.NewGuid()).Value,
            "ms",
            "  Anna  ",
            "Petrova",
            new DateOnly(1990, 1, 1),
            "female",
            "anna@example.test",
            "+79161234567",
            new DateOnly(2026, 6, 1)
        );
        result.IsError.ShouldBeFalse();
        result.Value.Passenger.GivenName.ShouldBe("Anna");
        result.Value.Passenger.Gender.ShouldBe(Gender.Female);
        result.Value.Title.Code.ShouldBe("ms");
    }

    [Theory]
    [InlineData(
        "                    Ivan",
        "mr",
        "male",
        "+79161234567",
        "Flights.PassengerGivenNameInvalid"
    )]
    [InlineData("Ivan", "MR", "male", "+79161234567", "Flights.PassengerTitleInvalid")]
    [InlineData("Ivan", "mr", "unspecified", "+79161234567", "Flights.PassengerGenderInvalid")]
    [InlineData("Ivan", "mr", "male", "+791612345\u06667", "Flights.PassengerPhoneInvalid")]
    public void Raw_factory_rejects_before_legacy_normalization(
        string name,
        string title,
        string gender,
        string phone,
        string code
    )
    {
        var id = BookingPassengerId.Create(Guid.NewGuid()).Value;
        var result = BookingPassengerDetails.CreateRaw(
            id,
            title,
            name,
            "Petrov",
            new DateOnly(1990, 1, 1),
            gender,
            "ivan@example.test",
            phone,
            new DateOnly(2026, 6, 1)
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(code);
        result.FirstError.Metadata!["bookingPassengerId"].ShouldBe(id.Value);
    }
}
