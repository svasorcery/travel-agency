using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Api.Contracts;

public static class CustomerRefundRequestValidation
{
    internal static Error Invalid =>
        Error.Validation(
            "Flights.CustomerRefundRequestInvalid",
            "Customer refund request invalid."
        );
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static bool Valid(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && CancellationRequestValidation.Unique(document.RootElement)
                && JsonSerializer.Deserialize<CustomerRefundReviewRequest>(bytes.Span, Options)
                    is { } request
                && !request.ToInput().IsError;
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
}
