using System.Security.Claims;

namespace Travel.Shared.Web;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        userId = default;
        var foundIdentifier = false;
        foreach (
            var claim in principal
                .FindAll(ClaimTypes.NameIdentifier)
                .Concat(principal.FindAll("sub"))
        )
        {
            if (!Guid.TryParse(claim.Value, out var identifier) || identifier == default)
            {
                userId = default;
                return false;
            }

            if (foundIdentifier && identifier != userId)
            {
                userId = default;
                return false;
            }

            userId = identifier;
            foundIdentifier = true;
        }

        return foundIdentifier;
    }
}
