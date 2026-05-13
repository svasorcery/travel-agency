using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class PassengerInfoTests
{
    private static PhoneNumber ValidPhone => PhoneNumber.Create("+79161234567").Value;

    private static DateOnly ValidDob => new(1990, 6, 15);

    [Fact]
    public void Create_returns_value_for_valid_input()
    {
        var r = PassengerInfo.Create(
            "Ivan",
            "Ivanov",
            ValidDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone
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
            ValidPhone
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
            ValidPhone
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.FamilyNameEmpty");
    }

    [Fact]
    public void Create_returns_error_when_dob_is_in_the_future()
    {
        var futureDob = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var r = PassengerInfo.Create(
            "Ivan",
            "Ivanov",
            futureDob,
            Gender.Male,
            "ivan@example.com",
            ValidPhone
        );

        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PassengerInfo.DateOfBirthFuture");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@nodomain")]
    [InlineData("missing-at-sign.com")]
    public void Create_returns_error_for_invalid_email(string email)
    {
        var r = PassengerInfo.Create("Ivan", "Ivanov", ValidDob, Gender.Male, email, ValidPhone);

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
            ValidPhone
        );

        r.IsError.ShouldBeFalse();
        r.Value.GivenName.ShouldBe("Ivan");
        r.Value.FamilyName.ShouldBe("Ivanov");
    }
}
