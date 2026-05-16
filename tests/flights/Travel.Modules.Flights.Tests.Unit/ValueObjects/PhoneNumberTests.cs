using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("+79161234567")]
    [InlineData("+11234567890")]
    [InlineData("+380501234567")]
    public void Create_returns_value_for_valid_e164_format(string input)
    {
        var r = PhoneNumber.Create(input);
        r.IsError.ShouldBeFalse();
        r.Value.Value.ShouldBe(input);
    }

    [Theory]
    [InlineData("79161234567")]
    [InlineData("+0")]
    [InlineData("+01234567")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_returns_validation_error_for_invalid_format(string input)
    {
        var r = PhoneNumber.Create(input);
        r.IsError.ShouldBeTrue();
        r.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = PhoneNumber.Create("+79161234567").Value;
        var b = PhoneNumber.Create("+79161234567").Value;
        a.ShouldBe(b);
    }

    // ── E.164 boundary: 15 total digits accepted, 16 rejected ─────────────────

    [Fact]
    public void Create_accepts_15_digit_number()
    {
        // +1 + 14 more digits = 15 total digits (E.164 maximum)
        var r = PhoneNumber.Create("+123456789012345");
        r.IsError.ShouldBeFalse();
    }

    [Fact]
    public void Create_rejects_16_digit_number()
    {
        // +1 + 15 more digits = 16 total digits (exceeds E.164 maximum)
        var r = PhoneNumber.Create("+1234567890123456");
        r.IsError.ShouldBeTrue();
        r.FirstError.Code.ShouldBe("PhoneNumber.Format");
    }
}
