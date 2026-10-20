using System.Globalization;
using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Core;
using Travel.Shared.Web;

namespace Travel.Modules.Trips.Api.Endpoints;

public static class TripsEndpoint
{
    public static ErrorOr<OwnerIdentity> Owner(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return Error.Unauthorized("Trips.Unauthenticated", "Authentication required.");
        if (!context.User.TryGetUserId(out var subject))
            return TripErrors.Forbidden;
        var issuers = context
            .User.FindAll("iss")
            .Select(x => x.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (issuers.Length != 1 || string.IsNullOrWhiteSpace(issuers[0]))
            return TripErrors.Forbidden;
        var subjects = context
            .User.FindAll("sub")
            .Concat(context.User.FindAll(ClaimTypes.NameIdentifier))
            .ToArray();
        if (!subjects.Any(claim => claim.Issuer == issuers[0]))
            return TripErrors.Forbidden;
        if (
            subjects.Any(claim =>
                claim.Issuer != issuers[0]
                && !(
                    claim.Type == ClaimTypes.NameIdentifier
                    && claim.Issuer == ClaimsIdentity.DefaultIssuer
                )
            )
        )
            return TripErrors.Forbidden;
        return OwnerIdentity.Create(issuers[0], subject);
    }

    public static ErrorOr<long> Revision(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("If-Match", out var raw))
            return Error.Custom(428, "Trips.PreconditionRequired", "A revision is required.");
        var value = raw.ToString();
        if (
            raw.Count != 1
            || value.Length < 3
            || value[0] != '"'
            || value[^1] != '"'
            || !long.TryParse(
                value.AsSpan(1, value.Length - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision
            )
            || revision <= 0
            || value != "\"" + revision.ToString(CultureInfo.InvariantCulture) + "\""
        )
            return TripErrors.InvalidInput;
        return revision;
    }

    public static IResult Failure(Error error) =>
        error.Code == "Trips.Unauthenticated" ? TripsProblems.Problem(error.Code, 401)
        : error.Code == "Trips.PreconditionRequired" ? TripsProblems.Problem(error.Code, 428)
        : TripsProblems.Error(error);

    public static IResult Json(string json, int status = 200) =>
        Results.Text(json, "application/json", System.Text.Encoding.UTF8, status);
}
