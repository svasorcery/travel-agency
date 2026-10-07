using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Api.Contracts;

public static class CancellationRequestValidation
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        NumberHandling = JsonNumberHandling.Strict,
    };
    internal static Error Invalid =>
        Error.Validation("Flights.CancellationCommandInvalid", "Cancellation request invalid.");

    internal static bool EnumValue<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return text is { Length: > 0 }
            && char.IsAsciiLetter(text[0])
            && Enum.TryParse(text, false, out value)
            && Enum.IsDefined(value)
            && Convert.ToInt32(value) != 0
            && string.Equals(Enum.GetName(value), text, StringComparison.Ordinal);
    }

    public static bool Valid(ReadOnlyMemory<byte> bytes, string route)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !Unique(document.RootElement)
            )
                return false;
            return route switch
            {
                "prepare" => JsonSerializer.Deserialize<PrepareCancellationRequest>(
                    bytes.Span,
                    Options
                )
                    is {
                        AggregateId: var aggregate,
                        OperationId: var op,
                        ExpectedBookingVersion: > 0
                    }
                    && aggregate != Guid.Empty
                    && op != Guid.Empty,
                "consent" => JsonSerializer.Deserialize<ConsentCancellationRequest>(
                    bytes.Span,
                    Options
                )
                    is {
                        AggregateId: var aggregate,
                        OperationId: var op,
                        ExpectedOperationRevision: > 0,
                        TermsRevision: > 0,
                        Accepted: true,
                        NoticeVersion: "cancellation-v1",
                        TermsHash: { Length: 64 } hash
                    }
                    && aggregate != Guid.Empty
                    && op != Guid.Empty
                    && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'),
                "abandon" => JsonSerializer.Deserialize<AbandonCancellationRequest>(
                    bytes.Span,
                    Options
                )
                    is {
                        AggregateId: var aggregate,
                        OperationId: var op,
                        ExpectedOperationRevision: > 0
                    }
                    && aggregate != Guid.Empty
                    && op != Guid.Empty,
                "refresh" or "review/refresh" =>
                    JsonSerializer.Deserialize<RefreshCancellationRequest>(bytes.Span, Options)
                        is {
                            AggregateId: var aggregate,
                            OperationId: var op,
                            ExpectedOperationRevision: > 0,
                            RefreshRequestId: var request
                        }
                        && aggregate != Guid.Empty
                        && op != Guid.Empty
                        && request != Guid.Empty,
                "review/resolve" => JsonSerializer.Deserialize<ResolveCancellationReviewRequest>(
                    bytes.Span,
                    Options
                )
                    is { } request
                    && !request.ToInput().IsError,
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !Unique(property.Value))
                    return false;
                if (
                    property.Name.EndsWith("At", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                )
                {
                    var timestamp = property.Value.GetString()!;
                    if (
                        !(
                            timestamp.EndsWith('Z')
                            || System.Text.RegularExpressions.Regex.IsMatch(
                                timestamp,
                                @"[+-][0-9]{2}:[0-9]{2}$"
                            )
                        )
                    )
                        return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (!Unique(item))
                    return false;
        return true;
    }
}
