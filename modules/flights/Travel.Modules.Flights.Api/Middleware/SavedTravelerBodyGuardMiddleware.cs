using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Middleware;

public sealed class SavedTravelerBodyGuardMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> Fields = new(
        ["title", "givenName", "familyName", "dateOfBirth", "gender", "email", "phone"],
        StringComparer.OrdinalIgnoreCase
    );

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> options
    )
    {
        if (
            !SavedTravelerResponsePolicyMiddleware.IsProfilePath(context.Request.Path)
            || !(
                HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsDelete(context.Request.Method)
            )
        )
        {
            await next(context);
            return;
        }
        if (!context.User.TryGetUserId(out _))
        {
            await context.WriteProblemDetailsAsync(IdentityProblemDetails.InvalidUserIdentity());
            return;
        }
        await BoundedRequestBody.ReadAsync(
            context,
            async bytes =>
            {
                if (
                    HttpMethods.IsDelete(context.Request.Method)
                        ? bytes.Length != 0
                        : !IsSafeJson(bytes, options.Value.SerializerOptions)
                )
                {
                    await context.WriteProblemDetailsAsync(SavedTravelerProblemDetails.Invalid());
                    return;
                }
                await next(context);
            },
            () =>
                context.WriteProblemDetailsAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = 413,
                        Type = "https://travel.local/errors/Flights.RequestTooLarge",
                        Title = "Request too large",
                        Detail = "Saved traveler request body exceeds the 16 KiB limit.",
                    }
                )
        );
    }

    private static bool IsSafeJson(ReadOnlyMemory<byte> bytes, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    MaxDepth = options.MaxDepth,
                    AllowTrailingCommas = options.AllowTrailingCommas,
                    CommentHandling = options.ReadCommentHandling,
                }
            );
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (
                    !Fields.Contains(property.Name)
                    || !seen.Add(property.Name)
                    || property.Value.ValueKind != JsonValueKind.String
                )
                    return false;
            }
            // Validate only binding safety here. Domain factories remain the source of business rules.
            return JsonSerializer.Deserialize<SavedTravelerDetailsDto>(bytes.Span, options)
                is not null;
        }
        catch (JsonException)
        {
            // Deliberately do not log: serializer exceptions can include submitted property names or values.
            return false;
        }
    }
}
