using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class SavedTravelerDetailsTests
{
    private static readonly DateOnly Today = new(2026, 10, 3);

    [Fact]
    public void Profile_has_no_slot_identity_and_can_be_saved_before_adult_departure()
    {
        var profile = Raw(dob: new DateOnly(2009, 10, 3));
        profile.IsError.ShouldBeFalse();
        profile.Value.ValidateForProfile(Today).IsError.ShouldBeFalse();
        profile.Value.ToString().ShouldBe("SavedTravelerDetails");
        var json = JsonSerializer.Serialize(profile.Value);
        json.ShouldNotContain("bookingPassengerId");
        json.ShouldNotContain("travelerId");
        var booking = BookingPassengerDetails
            .Create(profile.Value.Passenger, profile.Value.Title)
            .Value;
        booking
            .ValidateForTravel(new(2027, 10, 2), Today)
            .FirstError.Code.ShouldBe("Flights.PassengerAdultRequiredInvalid");
        booking.ValidateForTravel(new(2027, 10, 3), Today).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("givenName", "", "Flights.PassengerGivenNameInvalid")]
    [InlineData("givenName", "ABCDEFGHIJKLMNOPQRSTU", "Flights.PassengerGivenNameInvalid")]
    [InlineData("givenName", "Иван", "Flights.PassengerGivenNameInvalid")]
    [InlineData("givenName", "---", "Flights.PassengerGivenNameInvalid")]
    [InlineData("givenName", "Æ", "Flights.PassengerGivenNameInvalid")]
    [InlineData("familyName", "ABCDEFGHIJKLMNOPQRSTU", "Flights.PassengerFamilyNameInvalid")]
    [InlineData("title", "MR", "Flights.PassengerTitleInvalid")]
    [InlineData("title", "raw-sentinel@example.test", "Flights.PassengerTitleInvalid")]
    [InlineData("gender", "unspecified", "Flights.PassengerGenderInvalid")]
    [InlineData("gender", "M", "Flights.PassengerGenderInvalid")]
    [InlineData("email", " raw-sentinel@example.test", "Flights.PassengerEmailInvalid")]
    [InlineData("email", "Fixture <raw-sentinel@example.test>", "Flights.PassengerEmailInvalid")]
    [InlineData("phone", "+12025550٦23", "Flights.PassengerPhoneInvalid")]
    [InlineData("phone", "+012345678", "Flights.PassengerPhoneInvalid")]
    [InlineData("phone", "+1234567", "Flights.PassengerPhoneInvalid")]
    [InlineData("phone", "+1234567890123456", "Flights.PassengerPhoneInvalid")]
    public void Common_raw_validation_preserves_safe_slot_metadata_only_for_booking(
        string field,
        string value,
        string code
    )
    {
        var profile = Raw(field, value);
        profile.IsError.ShouldBeTrue();
        profile.FirstError.Code.ShouldBe(code);
        profile.FirstError.Description.ShouldNotContain("raw-sentinel");
        profile.FirstError.Metadata.ShouldBeNull();
        var slotFree = BookingRaw(field, value);
        slotFree.FirstError.Code.ShouldBe(code);
        slotFree.FirstError.Metadata.ShouldBeNull();
        var id = BookingPassengerId.Create(Guid.NewGuid()).Value;
        var slotted = BookingPassengerDetails.CreateRaw(
            id,
            field == "title" ? value : "mr",
            field == "givenName" ? value : "Fixture",
            field == "familyName" ? value : "Person",
            new(1990, 1, 1),
            field == "gender" ? value : "male",
            field == "email" ? value : "fictional@example.test",
            field == "phone" ? value : "+12025550123",
            Today
        );
        slotted.FirstError.Code.ShouldBe(code);
        slotted.FirstError.Metadata!["bookingPassengerId"].ShouldBe(id.Value);
        slotted.FirstError.Metadata["field"].ShouldBe(field);
    }

    [Theory]
    [InlineData("mr", "male")]
    [InlineData("ms", "female")]
    [InlineData("mrs", "female")]
    [InlineData("miss", "female")]
    [InlineData("dr", "male")]
    public void Valid_boundaries_and_explicit_enums_are_accepted(string title, string gender)
    {
        var result = SavedTravelerDetails.CreateRaw(
            title,
            "ABCDEFGHIJKLMNOPQRST",
            "D'Ángelo-Smith",
            new(1990, 1, 1),
            gender,
            "fictional@example.test",
            "+123456789012345",
            Today
        );
        result.IsError.ShouldBeFalse();
        Raw("givenName", "  Anna  ").Value.Passenger.GivenName.ShouldBe("Anna");
        Raw("phone", "+12345678").IsError.ShouldBeFalse();
    }

    [Fact]
    public void Missing_and_future_birth_dates_fail_but_structural_validation_has_no_clock()
    {
        Raw(dob: DateOnly.MinValue).FirstError.Code.ShouldBe("Flights.PassengerDateOfBirthInvalid");
        Raw(dob: Today.AddDays(1))
            .FirstError.Code.ShouldBe("Flights.PassengerDateOfBirthFutureInvalid");
        Raw(dob: Today).IsError.ShouldBeFalse();
        var futureInfo = PassengerInfo
            .Create(
                "Fixture",
                "Person",
                Today.AddDays(1),
                Gender.Male,
                "fictional@example.test",
                PhoneNumber.Create("+12025550123").Value,
                DateOnly.MaxValue
            )
            .Value;
        var future = SavedTravelerDetails
            .Create(futureInfo, PassengerTitle.Create("mr").Value)
            .Value;
        future.Validate().IsError.ShouldBeFalse();
        future
            .ValidateForProfile(Today)
            .FirstError.Code.ShouldBe("Flights.PassengerDateOfBirthFutureInvalid");
        SavedTravelerDetails
            .Create(null!, PassengerTitle.Create("mr").Value)
            .IsError.ShouldBeTrue();
        SavedTravelerDetails.Create(futureInfo, null!).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_historical_shapes_are_validated_without_changing_replay_constructors()
    {
        var valid = Raw().Value;
        foreach (
            var json in new[]
            {
                "{\"passenger\":null,\"title\":null}",
                JsonSerializer.Serialize(valid).Replace("male", "raw-sentinel"),
                JsonSerializer.Serialize(valid).Replace("1990-01-01", "0001-01-01"),
            }
        )
        {
            var profile = JsonSerializer.Deserialize<SavedTravelerDetails>(json)!;
            profile.Validate().IsError.ShouldBeTrue();
        }
        var leap = Raw(dob: new DateOnly(2008, 2, 29)).Value;
        BookingPassengerDetails
            .Create(leap.Passenger, leap.Title)
            .Value.ValidateForTravel(new(2026, 2, 28), Today)
            .IsError.ShouldBeFalse();
        SavedTravelerId.Create(Guid.Empty).IsError.ShouldBeTrue();
        var id = Guid.NewGuid();
        SavedTravelerId.Create(id).Value.Value.ShouldBe(id);
        Raw("email", new string('a', 241) + "@example.test").IsError.ShouldBeFalse();
        Raw("givenName", "É").IsError.ShouldBeFalse();
        var overlongEmail = new string('a', 242) + "@example.test";
        Raw("email", overlongEmail).FirstError.Code.ShouldBe("Flights.PassengerEmailInvalid");
        BookingPassengerDetails
            .CreateRaw(
                default(BookingPassengerId),
                "mr",
                "Fixture",
                "Person",
                new(1990, 1, 1),
                "male",
                "fictional@example.test",
                "+12025550123",
                Today
            )
            .FirstError.Code.ShouldBe("Flights.PassengerIdInvalid");
    }

    private static ErrorOr.ErrorOr<SavedTravelerDetails> Raw(
        string? field = null,
        string? value = null,
        DateOnly? dob = null
    ) =>
        SavedTravelerDetails.CreateRaw(
            field == "title" ? value! : "mr",
            field == "givenName" ? value! : "Fixture",
            field == "familyName" ? value! : "Person",
            dob ?? new(1990, 1, 1),
            field == "gender" ? value! : "male",
            field == "email" ? value! : "fictional@example.test",
            field == "phone" ? value! : "+12025550123",
            Today
        );

    private static ErrorOr.ErrorOr<BookingPassengerDetails> BookingRaw(
        string field,
        string value
    ) =>
        BookingPassengerDetails.CreateRaw(
            field == "title" ? value : "mr",
            field == "givenName" ? value : "Fixture",
            field == "familyName" ? value : "Person",
            new(1990, 1, 1),
            field == "gender" ? value : "male",
            field == "email" ? value : "fictional@example.test",
            field == "phone" ? value : "+12025550123",
            Today
        );
}
