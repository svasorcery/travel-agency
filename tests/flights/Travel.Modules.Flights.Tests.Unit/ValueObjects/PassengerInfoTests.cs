using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class PassengerInfoTests
{
    private static PhoneNumber ValidPhone => PhoneNumber.Create("+79161234567").Value;

    private static DateOnly ValidDob => new(1990, 6, 15);

    private static DateOnly ValidToday => new(2026, 5, 14);

    [Fact]
    public void Create_returns_value_for_valid_input()
    {
        var r = PassengerInfo.Create(
            "Ivan",
            "Ivanov",
            ValidDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone,
            ValidToday
        );

        r.IsError.ShouldBeFalse();
        r.Value.GivenName.ShouldBe("Ivan");
        r.Value.FamilyName.ShouldBe("Ivanov");
        r.Value.DateOfBirth.ShouldBe(ValidDob);
        r.Value.Email.ShouldBe("ivan@example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_returns_error_for_blank_given_name(string givenName)
    {
        var r = PassengerInfo.Create(
            givenName,
            "Ivanov",
            ValidDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone,
            ValidToday
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.GivenNameEmpty");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_returns_error_for_blank_family_name(string familyName)
    {
        var r = PassengerInfo.Create(
            "Ivan",
            familyName,
            ValidDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone,
            ValidToday
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.FamilyNameEmpty");
    }

    [Fact]
    public void Create_returns_error_when_dob_is_in_the_future()
    {
        var today = new DateOnly(2026, 5, 14);
        var futureDob = today.AddDays(1);

        var r = PassengerInfo.Create(
            "Ivan",
            "Ivanov",
            futureDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone,
            today
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.DateOfBirthFuture");
    }

    [Fact]
    public void Create_rejects_date_of_birth_after_the_supplied_today()
    {
        var today = new DateOnly(2026, 5, 14);
        var tomorrow = today.AddDays(1);

        var result = PassengerInfo.Create(
            "Ann",
            "Lee",
            tomorrow,
            Gender.Female,
            "ann@example.com",
            PhoneNumber.Create("+79161234567").Value,
            today
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("PassengerInfo.DateOfBirthFuture");
    }

    [Fact]
    public void Create_accepts_date_of_birth_equal_to_the_supplied_today()
    {
        var today = new DateOnly(2026, 5, 14);

        var result = PassengerInfo.Create(
            "Ann",
            "Lee",
            today,
            Gender.Female,
            "ann@example.com",
            PhoneNumber.Create("+79161234567").Value,
            today
        );

        result.IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@nodomain")]
    [InlineData("missing-at-sign.com")]
    public void Create_returns_error_for_invalid_email(string email)
    {
        var r = PassengerInfo.Create(
            "Ivan",
            "Ivanov",
            ValidDob,
            Gender.Male,
            email,
            ValidPhone,
            ValidToday
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.EmailInvalid");
    }

    [Fact]
    public void Create_trims_names_before_storing()
    {
        var r = PassengerInfo.Create(
            "  Ivan  ",
            "  Ivanov  ",
            ValidDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone,
            ValidToday
        );

        r.IsError.ShouldBeFalse();
        r.Value.GivenName.ShouldBe("Ivan");
        r.Value.FamilyName.ShouldBe("Ivanov");
    }
}
