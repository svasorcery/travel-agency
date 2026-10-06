using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Travel.Host.Tests.Integration.Flights;

internal static class ProfileJwtTestTokens
{
    internal const string Issuer = "profile-test";
    internal const string Audience = "travel-host";
    private const string SigningKey = "fictional-profile-test-signing-key-32-bytes";

    internal static SymmetricSecurityKey ValidationKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    internal static string Create(
        Guid owner,
        string failure = "none",
        string scope = "flights:book"
    )
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            failure == "issuer" ? "other-issuer" : Issuer,
            failure == "audience" ? "other-host" : Audience,
            [
                new Claim("sub", failure == "subject" ? "invalid-sub" : owner.ToString("D")),
                new Claim("scope", failure == "scope" ? "flights:read" : scope),
            ],
            failure == "expired" ? now.AddMinutes(-20) : now.AddMinutes(-1),
            failure == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(
                failure == "signature"
                    ? new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("fictional-wrong-profile-signing-key-32-bytes")
                    )
                    : ValidationKey(),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
