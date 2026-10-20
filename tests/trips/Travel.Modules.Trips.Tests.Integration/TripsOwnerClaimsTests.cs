using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Travel.Modules.Trips.Api.Endpoints;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsOwnerClaimsTests
{
    [Fact]
    public void Trusted_subject_and_generated_name_identifier_agree_but_local_only_subject_is_forbidden()
    {
        var context = new DefaultHttpContext();
        var issuer = "https://fictional.issuer.test";
        var subject = Guid.NewGuid().ToString();
        var iss = new Claim("iss", issuer, ClaimValueTypes.String, issuer);
        context.User = new(
            new ClaimsIdentity(
                [
                    iss,
                    new Claim("sub", subject, ClaimValueTypes.String, issuer),
                    new Claim(ClaimTypes.NameIdentifier, subject),
                ],
                "fictional"
            )
        );
        TripsEndpoint.Owner(context).IsError.ShouldBeFalse();
        context.User = new(
            new ClaimsIdentity([iss, new Claim(ClaimTypes.NameIdentifier, subject)], "fictional")
        );
        TripsEndpoint.Owner(context).FirstError.Code.ShouldBe("Trips.Forbidden");
        context.User = new(
            new ClaimsIdentity(
                [
                    iss,
                    new Claim(
                        "sub",
                        subject,
                        ClaimValueTypes.String,
                        "https://conflicting.issuer.test"
                    ),
                ],
                "fictional"
            )
        );
        TripsEndpoint.Owner(context).FirstError.Code.ShouldBe("Trips.Forbidden");
    }
}
