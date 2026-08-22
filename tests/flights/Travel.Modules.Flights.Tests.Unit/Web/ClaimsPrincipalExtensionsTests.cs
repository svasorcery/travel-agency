using System.Security.Claims;
using Shouldly;
using Travel.Shared.Web;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Web;

public sealed class ClaimsPrincipalExtensionsTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(ClaimTypes.NameIdentifier)]
    [InlineData("sub")]
    public void TryGetUserId_accepts_one_non_empty_supported_identifier(string claimType)
    {
        var principal = Principal(new Claim(claimType, UserId.ToString()));

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeTrue();
        userId.ShouldBe(UserId);
    }

    [Fact]
    public void TryGetUserId_accepts_repeated_equal_identifiers_across_supported_claim_types()
    {
        var principal = Principal(
            new Claim("sub", UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim("sub", UserId.ToString())
        );

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeTrue();
        userId.ShouldBe(UserId);
    }

    [Fact]
    public void TryGetUserId_rejects_missing_supported_identifier()
    {
        var principal = Principal(new Claim("email", "traveler@example.test"));

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeFalse();
        userId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void TryGetUserId_rejects_any_malformed_supported_identifier()
    {
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim("sub", "not-a-guid")
        );

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeFalse();
        userId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void TryGetUserId_rejects_any_empty_guid_identifier()
    {
        var principal = Principal(
            new Claim("sub", UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString())
        );

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeFalse();
        userId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void TryGetUserId_rejects_conflicting_valid_identifiers()
    {
        var principal = Principal(
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim("sub", OtherUserId.ToString())
        );

        var success = principal.TryGetUserId(out var userId);

        success.ShouldBeFalse();
        userId.ShouldBe(Guid.Empty);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
