using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Travel.Host.Tests.Integration.Support;

internal static class SupportJwtTestTokens
{
    internal const string Issuer = "fictional-support-issuer";
    internal const string Audience = "travel-web";

    internal static SymmetricSecurityKey Key() =>
        new(Encoding.UTF8.GetBytes("fictional-support-test-signing-key-32-bytes"));

    internal static string Create(
        Guid subject,
        string scope = "openid",
        string azp = "travel-web",
        string failure = "none"
    )
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            failure == "issuer" ? "other" : Issuer,
            failure == "audience" ? "other" : Audience,
            [
                new("sub", failure == "subject" ? "invalid" : subject.ToString("D")),
                new("scope", scope),
                new("azp", azp),
            ],
            failure == "expired" ? now.AddHours(-2) : now.AddMinutes(-1),
            failure == "expired" ? now.AddHours(-1) : now.AddMinutes(5),
            new SigningCredentials(
                failure == "signature"
                    ? new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("wrong-fictional-support-test-key-32-bytes")
                    )
                    : Key(),
                SecurityAlgorithms.HmacSha256
            )
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
