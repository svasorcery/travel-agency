using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class DuffelWebhookEndpoint
{
    private const string SignatureHeaderName = "X-Duffel-Signature";

    // Historical direct-call compatibility helper. The only network route is Host-owned.
    [AllowAnonymous]
    public static async Task<IResult> Receive(
        HttpRequest request,
        [FromServices] IWebhookIngestionService ingestionService,
        CancellationToken ct
    )
    {
        if (
            !request.Headers.TryGetValue(SignatureHeaderName, out var signatureValues)
            || signatureValues.Count != 1
        )
        {
            return Results.Problem(
                new[] { WebhookIngestionErrors.InvalidSignature }.ToList().ToProblemDetails()
            );
        }

        using var body = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) != 0)
        {
            if (body.Length + read > 2 * 1024 * 1024)
                return Results.Problem(statusCode: 413);
            body.Write(chunk, 0, read);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
            headers[header.Key] = header.Value.ToString();

        var result = await ingestionService.IngestAsync(
            new WebhookIngestionRequest("duffel", body.ToArray(), headers),
            ct
        );

        return result.IsError ? Results.Problem(result.Errors.ToProblemDetails()) : Results.Ok();
    }
}
