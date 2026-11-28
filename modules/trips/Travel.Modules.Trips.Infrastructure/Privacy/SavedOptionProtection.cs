using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;
using Travel.Modules.Trips.Infrastructure.Serialization;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class SavedOptionProtection(TripsProtectionProvider keys) : ISavedOptionProtection
{
    public const int MaximumPreviewBytes = 32 * 1024;
    public const int MaximumPlaintextBytes = 40 * 1024;
    public const int MaximumEnvelopeLength = 64 * 1024;
    public static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 20,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        AllowDuplicateProperties = false,
    };

    public ErrorOr<SavedOptionSelection> Issue(
        OwnerIdentity owner,
        TripBriefInput brief,
        TripOptionPreview preview,
        DateTimeOffset now
    )
    {
        var normalized = TripBrief.Create(brief);
        if (
            normalized.IsError
            || !HistoricalOptionV1.Matches(normalized.Value.ToInput(), preview)
            || now.Offset != TimeSpan.Zero
        )
            return TripErrors.InvalidInput;
        if (JsonSerializer.SerializeToUtf8Bytes(preview, Json).Length > MaximumPreviewBytes)
            return TripErrors.PlanTooLarge;
        var id = Guid.NewGuid();
        var issued = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        var expiry = issued.Add(SelectionLifetime);
        var token = Seal(
            JsonSerializer.SerializeToUtf8Bytes(
                new IssuedOption(1, id, issued, expiry, normalized.Value.ToInput(), preview),
                Json
            ),
            SelectionPurpose(owner)
        );
        return token.IsError ? token.Errors : new SavedOptionSelection(id, expiry, token.Value);
    }

    public ErrorOr<IssuedOption> OpenSelection(OwnerIdentity owner, string token)
    {
        if (!keys.IsReady)
            return TripErrors.Unavailable;
        var opened = Open<IssuedOption>(token, SelectionPurpose(owner));
        if (opened.IsError)
            return TripErrors.SelectionUnavailable;
        var selection = opened.Value;
        // Expiry is enforced for first writes by PostgreSQL after taking the owner's lock.
        return
            selection.Version == 1
            && selection.TripId != Guid.Empty
            && selection.IssuedAtUtc.Offset == TimeSpan.Zero
            && selection.ExpiresAtUtc.Offset == TimeSpan.Zero
            && selection.ExpiresAtUtc - selection.IssuedAtUtc == SelectionLifetime
            && HistoricalOptionV1.Matches(selection.Brief, selection.Preview)
            ? selection
            : TripErrors.SelectionUnavailable;
    }

    public ErrorOr<string> ProtectHistory(
        OwnerIdentity owner,
        Guid tripId,
        HistoricalTripOption option
    ) =>
        tripId != Guid.Empty && ValidHistory(option)
            ? Seal(JsonSerializer.SerializeToUtf8Bytes(option, Json), HistoryPurpose(owner, tripId))
            : TripErrors.Unavailable;

    public ErrorOr<HistoricalTripOption> OpenHistory(
        OwnerIdentity owner,
        Guid tripId,
        string envelope
    )
    {
        var result = Open<HistoricalTripOption>(envelope, HistoryPurpose(owner, tripId));
        return !result.IsError && ValidHistory(result.Value)
            ? result.Value
            : TripErrors.Unavailable;
    }

    private static bool ValidHistory(HistoricalTripOption value) =>
        value.Version == 1
        && value.SavedAtUtc != default
        && value.SavedAtUtc.Offset == TimeSpan.Zero
        && HistoricalOptionV1.Matches(value.Brief, value.Preview);

    private static string[] SelectionPurpose(OwnerIdentity owner) =>
        ["trip-option-selection", "1", owner.Issuer, owner.Subject.ToString("N")];

    private static string[] HistoryPurpose(OwnerIdentity owner, Guid id) =>
        ["trip-option-history", "1", owner.Issuer, owner.Subject.ToString("N"), id.ToString("N")];

    private ErrorOr<string> Seal(byte[] bytes, string[] purpose)
    {
        try
        {
            if (bytes.Length > MaximumPlaintextBytes)
                return TripErrors.PlanTooLarge;
            var encrypted = keys.Protect(bytes, purpose);
            if (encrypted.IsError)
                return TripErrors.Unavailable;
            return encrypted.Value.Length <= MaximumEnvelopeLength / 4 * 3
                ? Convert.ToBase64String(encrypted.Value)
                : TripErrors.PlanTooLarge;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private ErrorOr<T> Open<T>(string envelope, string[] purpose)
        where T : class
    {
        if (string.IsNullOrEmpty(envelope) || envelope.Length > MaximumEnvelopeLength)
            return TripErrors.Unavailable;
        try
        {
            var bytes = Convert.FromBase64String(envelope);
            if (!string.Equals(Convert.ToBase64String(bytes), envelope, StringComparison.Ordinal))
                return TripErrors.Unavailable;
            var result = keys.Unprotect(bytes, purpose);
            if (result.IsError)
                return TripErrors.Unavailable;
            try
            {
                if (result.Value.Length > MaximumPlaintextBytes)
                    return TripErrors.Unavailable;
                return JsonSerializer.Deserialize<T>(result.Value, Json) is { } value
                    ? value
                    : TripErrors.Unavailable;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(result.Value);
            }
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return TripErrors.Unavailable;
        }
    }
}
