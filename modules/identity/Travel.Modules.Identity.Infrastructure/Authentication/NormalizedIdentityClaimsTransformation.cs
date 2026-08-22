using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Travel.Modules.Identity.Infrastructure.Authentication;

public sealed class NormalizedIdentityClaimsTransformation : IClaimsTransformation
{
    private const string SubjectClaimType = "sub";
    private const string ScopeClaimType = "scope";
    private const string ScpClaimType = "scp";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        NormalizeNameIdentifier(principal);
        NormalizeScopes(principal);
        return Task.FromResult(principal);
    }

    private static void NormalizeNameIdentifier(ClaimsPrincipal principal)
    {
        var subjectClaims = principal.FindAll(SubjectClaimType).ToArray();
        if (subjectClaims.Length == 0)
            return;

        var parsedSubjectIdentifiers = subjectClaims
            .Select(claim =>
                TryParseNonEmptyGuid(claim.Value, out var identifier) ? (Guid?)identifier : null
            )
            .ToArray();
        if (parsedSubjectIdentifiers.Any(identifier => identifier is null))
            return;

        var subjectIdentifiers = parsedSubjectIdentifiers
            .Select(identifier => identifier!.Value)
            .Distinct()
            .ToArray();
        if (subjectIdentifiers.Length != 1)
            return;

        var subjectIdentifier = subjectIdentifiers[0];
        var existingNameIdentifiers = principal.FindAll(ClaimTypes.NameIdentifier).ToArray();
        if (existingNameIdentifiers.Length > 0)
        {
            if (
                existingNameIdentifiers.Any(claim =>
                    !TryParseNonEmptyGuid(claim.Value, out var identifier)
                    || identifier != subjectIdentifier
                )
            )
                return;

            return;
        }

        var identity = principal
            .Identities.OfType<ClaimsIdentity>()
            .FirstOrDefault(candidate =>
                candidate.Claims.Any(claim =>
                    claim.Type == SubjectClaimType
                    && TryParseNonEmptyGuid(claim.Value, out var identifier)
                    && identifier == subjectIdentifier
                )
            );
        identity?.AddClaim(
            new System.Security.Claims.Claim(
                ClaimTypes.NameIdentifier,
                subjectIdentifier.ToString()
            )
        );
    }

    private static void NormalizeScopes(ClaimsPrincipal principal)
    {
        var canonicalScopes = principal
            .Claims.Where(claim => claim.Type is ScopeClaimType or ScpClaimType)
            .SelectMany(claim =>
                claim.Value.Split(
                    [' '],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var identity in principal.Identities.OfType<ClaimsIdentity>())
        {
            foreach (
                var claim in identity
                    .Claims.Where(claim => claim.Type is ScopeClaimType or ScpClaimType)
                    .ToArray()
            )
            {
                identity.RemoveClaim(claim);
            }
        }

        var targetIdentity = principal.Identities.OfType<ClaimsIdentity>().FirstOrDefault();
        if (targetIdentity is null)
            return;

        foreach (var scope in canonicalScopes)
            targetIdentity.AddClaim(new System.Security.Claims.Claim(ScopeClaimType, scope));
    }

    private static bool TryParseNonEmptyGuid(string value, out Guid identifier) =>
        Guid.TryParse(value, out identifier) && identifier != Guid.Empty;
}
