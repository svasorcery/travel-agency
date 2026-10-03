using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Application.SavedTravelers;

namespace Travel.Modules.Flights.Api.Endpoints;

internal readonly record struct SavedTravelerCondition(bool Create, Guid Revision);

internal static class SavedTravelerPreconditions
{
    internal static ErrorOr<SavedTravelerCondition> Parse(HttpRequest request, bool allowCreate)
    {
        var hasMatch = request.Headers.TryGetValue("If-Match", out var match);
        var hasNone = request.Headers.TryGetValue("If-None-Match", out var none);
        if (!hasMatch && !hasNone)
            return SavedTravelerErrors.PreconditionRequired;
        if (hasMatch && hasNone)
            return SavedTravelerErrors.PreconditionInvalid;
        if (hasNone)
            return allowCreate && none.Count == 1 && none[0] == "*"
                ? new SavedTravelerCondition(true, Guid.Empty)
                : SavedTravelerErrors.PreconditionInvalid;
        var value = match.Count == 1 ? match[0] : null;
        if (
            value is not { Length: 38 }
            || value[0] != '"'
            || value[^1] != '"'
            || !Guid.TryParseExact(value.AsSpan(1, 36), "D", out var revision)
            || revision == Guid.Empty
            || value != ETag(revision)
        )
            return SavedTravelerErrors.PreconditionInvalid;
        return new SavedTravelerCondition(false, revision);
    }

    internal static string ETag(Guid revision) => $"\"{revision:D}\"";
}
