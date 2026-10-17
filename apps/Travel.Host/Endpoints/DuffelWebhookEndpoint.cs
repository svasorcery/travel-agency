using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Travel.Host.Configuration;
using Travel.Shared.Abstractions;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Host.Endpoints;

public sealed class DuffelWebhookEndpoint
{
    [WolverinePost("/webhooks/duffel")]
    [AllowAnonymous]
    public static async Task<IResult> Receive(
        HttpRequest request,
        [FromServices] IEnumerable<IRawWebhookReceiver> receivers,
        [FromServices] IOptions<RawWebhookTransportOptions> options,
        CancellationToken ct
    )
    {
        if (
            !request.Headers.TryGetValue("X-Duffel-Signature", out var signatures)
            || signatures.Count != 1
            || string.IsNullOrWhiteSpace(signatures[0])
        )
            return SignatureFailure();
        var maximum = options.Value.MaximumBytes;
        if (request.ContentLength > maximum)
            return Results.Problem(statusCode: 413, title: "Webhook payload is too large.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > maximum)
                return Results.Problem(statusCode: 413, title: "Webhook payload is too large.");
            buffer.Write(chunk, 0, count);
        }
        var delivery = new RawWebhookDelivery(buffer.ToArray(), signatures[0]!);
        var probes = receivers.Select(r => (Receiver: r, Probe: r.Probe(delivery))).ToArray();
        var owners = probes.Where(p => p.Probe.Kind == RawWebhookProbeKind.Owner).ToArray();
        if (owners.Length > 1)
            return Results.Problem(statusCode: 503, title: "Webhook ownership is ambiguous.");
        if (owners.Length == 1)
        {
            var result = await owners[0].Receiver.AcceptAsync(delivery, ct);
            return result switch
            {
                RawWebhookAcceptance.Accepted or RawWebhookAcceptance.Duplicate => Results.Ok(),
                RawWebhookAcceptance.InvalidSignature => SignatureFailure(),
                RawWebhookAcceptance.InvalidPayload => PayloadFailure(),
                _ => Results.Problem(statusCode: 503, title: "Webhook acceptance unavailable."),
            };
        }
        var requiredNames = probes
            .Where(p => p.Probe.Kind == RawWebhookProbeKind.RequiredReceiver)
            .Select(p => p.Probe.RequiredReceiver)
            .Where(name => name is not null)
            .ToArray();
        if (
            probes.Any(p =>
                p.Probe.Kind == RawWebhookProbeKind.InvalidPayload
                && requiredNames.Contains(p.Receiver.Name, StringComparer.Ordinal)
            )
        )
            return PayloadFailure();
        if (
            probes.Any(p =>
                p.Probe.Kind
                    is RawWebhookProbeKind.RequiredReceiver
                        or RawWebhookProbeKind.Unavailable
            )
        )
            return Results.Problem(
                statusCode: 503,
                title: "Required webhook receiver is unavailable."
            );
        if (probes.Any(p => p.Probe.Kind == RawWebhookProbeKind.InvalidPayload))
            return PayloadFailure();
        if (probes.Any(p => p.Probe.Kind == RawWebhookProbeKind.NotApplicable))
            return Results.Ok(new { ignored = true });
        return SignatureFailure();
    }

    private static IResult SignatureFailure() =>
        Results.Problem(
            new List<Error>
            {
                Error.Unauthorized(
                    "Flights.Webhook.InvalidSignature",
                    "Webhook signature is invalid."
                ),
            }.ToProblemDetails()
        );

    private static IResult PayloadFailure() =>
        Results.Problem(
            new List<Error>
            {
                Error.Validation("Flights.Webhook.InvalidPayload", "Webhook payload is invalid."),
            }.ToProblemDetails()
        );
}
