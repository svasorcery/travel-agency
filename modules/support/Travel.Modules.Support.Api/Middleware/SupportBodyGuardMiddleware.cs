using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Api.Middleware;

public sealed class SupportBodyGuardMiddleware(RequestDelegate next)
{
    public const int MaximumBytes = 32 * 1024;
    public const int MaximumClassificationBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task InvokeAsync(HttpContext context)
    {
        if (
            !SupportResponsePolicyMiddleware.IsSupportPath(context.Request.Path)
            || !(
                HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsPost(context.Request.Method)
                    && IsClassificationPath(context.Request.Path)
            )
        )
        {
            await next(context);
            return;
        }
        var isOperator = context.Request.Path.StartsWithSegments(
            "/api/support/operator",
            StringComparison.OrdinalIgnoreCase
        );
        var caller = SupportEndpoint.Caller(context, isOperator);
        if (caller.IsError)
        {
            await Problem(context, SupportProblemDetails.From(caller.Errors));
            return;
        }
        var maximum = IsClassificationPath(context.Request.Path)
            ? MaximumClassificationBytes
            : MaximumBytes;
        if (context.Request.ContentLength > maximum)
        {
            await Problem(context, SupportProblemDetails.Fixed(413));
            return;
        }
        if (
            !context.Request.HasJsonContentType()
            || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            || contentType.Charset.HasValue
                && !string.Equals(
                    contentType.Charset.Value?.Trim('"'),
                    "utf-8",
                    StringComparison.OrdinalIgnoreCase
                )
        )
        {
            await Problem(context, SupportProblemDetails.Fixed(400));
            return;
        }
        var original = context.Request.Body;
        var bytes = new byte[maximum + 1];
        try
        {
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await original.ReadAsync(bytes.AsMemory(length), context.RequestAborted);
                if (read == 0)
                    break;
                length += read;
            }
            if (length > maximum)
            {
                await Problem(context, SupportProblemDetails.Fixed(413));
                return;
            }
            if (!Valid(bytes.AsMemory(0, length), context.Request.Path, isOperator))
            {
                await Problem(context, SupportProblemDetails.Fixed(400));
                return;
            }
            using var body = new MemoryStream(bytes, 0, length, false);
            context.Request.Body = body;
            await next(context);
        }
        finally
        {
            context.Request.Body = original;
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static Task Problem(
        HttpContext context,
        Microsoft.AspNetCore.Mvc.ProblemDetails details
    )
    {
        context.Response.StatusCode = details.Status!.Value;
        return context.Response.WriteAsJsonAsync(
            details,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted
        );
    }

    private static bool Valid(ReadOnlyMemory<byte> bytes, PathString path, bool isOperator)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes.Span);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = 4 }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in document.RootElement.EnumerateObject())
                if (!fields.TryAdd(item.Name, item.Value))
                    return false;
            var segments = path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (IsClassificationPath(path))
            {
                return HasOnly(
                        fields,
                        ["requestId", "expectedRevision"],
                        ["requestId", "expectedRevision"]
                    )
                    && GuidField(fields["requestId"])
                    && IsV4(fields["requestId"].GetGuid())
                    && Revision(fields["expectedRevision"]);
            }
            var create = !isOperator && segments.Length == 4 && segments[2] == "cases";
            if (create)
                return HasOnly(
                        fields,
                        ["messageId", "title", "body", "orderId"],
                        ["messageId", "title", "body"]
                    )
                    && GuidField(fields["messageId"])
                    && Text(fields["title"], 120)
                    && Text(fields["body"], 4000)
                    && (
                        !fields.TryGetValue("orderId", out var order)
                        || order.ValueKind == JsonValueKind.Null
                        || GuidField(order)
                    );
            if (segments.Length != (isOperator ? 7 : 6))
                return false;
            var resource = segments[^2];
            if (resource == "messages")
                return HasOnly(
                        fields,
                        ["body", "expectedRevision", "kind"],
                        ["body", "expectedRevision", "kind"]
                    )
                    && Text(fields["body"], 4000)
                    && Revision(fields["expectedRevision"])
                    && fields["kind"].ValueKind == JsonValueKind.String
                    && (
                        fields["kind"].GetString() == "Reply"
                        || (!isOperator && fields["kind"].GetString() == "ReplyAndReopen")
                    );
            if (
                !isOperator
                || resource != "actions"
                || !fields.TryGetValue("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
            )
                return false;
            return kind.GetString() switch
            {
                "Resolve" => HasOnly(
                    fields,
                    ["kind", "expectedRevision"],
                    ["kind", "expectedRevision"]
                ) && Revision(fields["expectedRevision"]),
                "SetTriage" => HasOnly(
                    fields,
                    ["kind", "expectedRevision", "category", "priority"],
                    ["kind", "expectedRevision", "category", "priority"]
                )
                    && Revision(fields["expectedRevision"])
                    && EnumField<ManualCategory>(fields["category"])
                    && EnumField<SupportPriority>(fields["priority"]),
                _ => false,
            };
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsClassificationPath(PathString path)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is { Length: 6 }
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("support", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("operator", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("cases", StringComparison.OrdinalIgnoreCase)
            && segments[5].Equals("classification-suggestion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsV4(Guid value)
    {
        var text = value.ToString("N");
        return text[12] == '4' && "89ab".Contains(text[16]);
    }

    private static bool HasOnly(
        Dictionary<string, JsonElement> fields,
        string[] allowed,
        string[] required
    ) => fields.Keys.All(allowed.Contains) && required.All(fields.ContainsKey);

    private static bool GuidField(JsonElement field) =>
        field.ValueKind == JsonValueKind.String && field.TryGetGuid(out var id) && id != Guid.Empty;

    private static bool Revision(JsonElement field) =>
        field.ValueKind == JsonValueKind.Number
        && field.TryGetInt32(out var revision)
        && revision > 0;

    private static bool EnumField<T>(JsonElement field)
        where T : struct, Enum =>
        field.ValueKind == JsonValueKind.String
        && SupportEndpoint.TryEnum<T>(field.GetString(), out var value)
        && value is not null;

    private static bool Text(JsonElement field, int maximum)
    {
        if (field.ValueKind != JsonValueKind.String)
            return false;
        var text = field.GetString();
        if (string.IsNullOrWhiteSpace(text) || StrictUtf8.GetByteCount(text) > 16384)
            return false;
        var span = text.AsSpan();
        var count = 0;
        while (!span.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(span, out _, out var consumed) != OperationStatus.Done
                || ++count > maximum
            )
                return false;
            span = span[consumed..];
        }
        return true;
    }
}
