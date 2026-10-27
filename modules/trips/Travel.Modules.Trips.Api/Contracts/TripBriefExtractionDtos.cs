using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Modules.Trips.Api.Contracts;

public static class TripBriefExtractionDtos
{
    public const int MaximumBodyBytes = 8192;
    public const int MaximumResultBytes = 16384;
    public static readonly object InputKey = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Names =
    [
        "requestId",
        "text",
        "locale",
        "referenceZone",
        "processingPolicyVersion",
    ];

    public static ErrorOr<TripBriefExtractionInput> Read(
        ReadOnlyMemory<byte> bytes,
        DateTimeOffset anchor,
        DateTimeOffset deadline
    )
    {
        try
        {
            StrictUtf8.GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = 8 }
            );
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return TripBriefErrors.InvalidInput;
            var names = root.EnumerateObject().Select(item => item.Name).ToArray();
            if (
                names.Length != 5
                || names.Distinct(StringComparer.Ordinal).Count() != 5
                || names.Except(Names, StringComparer.Ordinal).Any()
            )
                return TripBriefErrors.InvalidInput;
            if (
                root.GetProperty("requestId").ValueKind != JsonValueKind.String
                || !root.GetProperty("requestId").TryGetGuid(out var id)
                || id == Guid.Empty
                || root.GetProperty("text").ValueKind != JsonValueKind.String
                || root.GetProperty("locale").ValueKind != JsonValueKind.String
                || root.GetProperty("processingPolicyVersion").ValueKind != JsonValueKind.String
                || root.GetProperty("referenceZone").ValueKind
                    is not (JsonValueKind.Null or JsonValueKind.String)
            )
                return TripBriefErrors.InvalidInput;
            var text = root.GetProperty("text").GetString()!;
            // Bounds are a distinct 413; malformed Unicode/controls remain a contract error.
            var utf8 = StrictUtf8.GetByteCount(text);
            if (text.EnumerateRunes().Count() > 1000 || utf8 > 4096)
                return Error.Custom(413, "Trips.BriefTooLarge", "Extraction request is too large.");
            var input = new TripBriefExtractionInput(
                id,
                root.GetProperty("processingPolicyVersion").GetString()!,
                text,
                root.GetProperty("locale").GetString()!,
                anchor,
                root.GetProperty("referenceZone").GetString(),
                deadline
            );
            return TripBriefCandidateValidator.ValidInput(input)
                ? input
                : TripBriefErrors.InvalidInput;
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or DecoderFallbackException
                        or EncoderFallbackException
                        or InvalidOperationException
                        or ArgumentException
            )
        {
            return TripBriefErrors.InvalidInput;
        }
    }

    public static ErrorOr<byte[]> Encode(TripBriefCandidate candidate)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                candidateVersion = candidate.CandidateVersion,
                requestId = candidate.RequestId,
                processingPolicyVersion = candidate.ProcessingPolicyVersion,
                referenceInstant = Utc(candidate.ReferenceInstant),
                referenceZone = candidate.ReferenceZone,
                referenceLocalDate = candidate.ReferenceLocalDate?.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                expiresAt = Utc(candidate.ExpiresAt),
                fields = candidate.Fields,
                issues = candidate.Issues,
            },
            JsonSerializerOptions.Web
        );
        if (bytes.Length <= MaximumResultBytes)
            return bytes;
        CryptographicOperations.ZeroMemory(bytes);
        return TripBriefErrors.InvalidOutput;
    }

    private static string Utc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture
        );

    public static IResult Failure(Error error) =>
        TripsProblems.Problem(
            error.Code,
            error.NumericType is 413 or 429 or 502 or 503 or 504 ? error.NumericType : 400
        );
}
