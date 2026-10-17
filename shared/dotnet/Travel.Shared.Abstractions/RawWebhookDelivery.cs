namespace Travel.Shared.Abstractions;

// Transport-only seam. No supplier DTO, payload persistence, module dependency or domain decision.
public sealed record RawWebhookDelivery(ReadOnlyMemory<byte> Bytes, string Signature);

public enum RawWebhookProbeKind
{
    Owner,
    NotApplicable,
    RequiredReceiver,
    InvalidSignature,
    InvalidPayload,
    Unavailable,
}

public sealed record RawWebhookProbe(RawWebhookProbeKind Kind, string? RequiredReceiver = null);

public enum RawWebhookAcceptance
{
    Accepted,
    Duplicate,
    InvalidSignature,
    InvalidPayload,
    Unavailable,
}

public interface IRawWebhookReceiver
{
    string Name { get; }
    RawWebhookProbe Probe(RawWebhookDelivery delivery);
    Task<RawWebhookAcceptance> AcceptAsync(
        RawWebhookDelivery delivery,
        CancellationToken cancellationToken
    );
}
