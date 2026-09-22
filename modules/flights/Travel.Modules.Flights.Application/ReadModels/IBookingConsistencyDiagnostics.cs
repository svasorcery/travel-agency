namespace Travel.Modules.Flights.Application.ReadModels;

public sealed record BookingEnvelopeDiagnostic(
    Guid MessageId,
    string MessageType,
    string State,
    bool Replayable
);

public sealed record BookingConsistencyInspection(
    ProjectionValidation Validation,
    IReadOnlyList<BookingEnvelopeDiagnostic> Envelopes
);

public sealed record BookingReplayResult(Guid MessageId, bool Replayable, string Code);

public interface IBookingConsistencyDiagnostics
{
    Task<BookingConsistencyInspection> InspectAsync(Guid aggregateId, CancellationToken ct);
    Task<BookingReplayResult> ReplayAsync(Guid envelopeId, CancellationToken ct);
}
