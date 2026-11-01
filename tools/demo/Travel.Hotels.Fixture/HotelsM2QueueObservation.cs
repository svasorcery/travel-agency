using System.Text.Json;
using System.Text.Json.Serialization;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Webhooks;
using Wolverine;
using Wolverine.Util;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2QueueRow(
    Guid Id,
    bool HasAttempt,
    bool HasBookingId,
    ReservationOutcome Outcome
)
{
    internal static HotelM2QueueRow Capture(
        Guid id,
        Guid? attempt,
        string? bookingId,
        ReservationOutcome outcome
    ) =>
        new(
            id,
            attempt is { } value && value != Guid.Empty,
            !string.IsNullOrWhiteSpace(bookingId),
            outcome
        );
}

internal sealed record HotelM2DueItem(
    Guid EnvelopeId,
    string MessageType,
    DateTimeOffset DueAt,
    int BodyBytes,
    Guid? ReservationId,
    string? Outcome,
    bool? Escalation,
    string Disposition,
    string Reason
);

internal sealed record HotelM2QueueSnapshot(
    int RawDue,
    int RequiredDue,
    int RedundantDue,
    int UnknownDue,
    bool Complete,
    HotelM2DueItem[] Items
);

internal sealed record HotelM2QueueDiagnostic(
    bool LegacyGatePassed,
    bool Blocking,
    HotelM2QueueSnapshot Final
);

internal static class HotelsM2QueueObservation
{
    internal const int MaximumItems = 64;
    internal const string MandatoryGate = "m2ActionableDueWorkAndInboxSettled";
    private static readonly string WorkType = typeof(HotelWork).ToMessageTypeName();
    private static readonly string InboxType = typeof(ProcessHotelObservation).ToMessageTypeName();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record WorkPayload(
        [property: JsonRequired] Guid ReservationId,
        [property: JsonRequired] bool Escalation,
        [property: JsonRequired] DateTimeOffset DueAt
    );

    internal static HotelM2QueueSnapshot Capture(
        IEnumerable<Envelope> envelopes,
        IEnumerable<HotelM2QueueRow> reservations,
        DateTimeOffset cutoff
    )
    {
        var rows = reservations.ToDictionary(r => r.Id);
        var due = envelopes
            .Where(e =>
                e.Status != EnvelopeStatus.Handled && (e.ScheduledTime ?? e.SentAt) <= cutoff
            )
            .OrderBy(e => e.ScheduledTime ?? e.SentAt)
            .ThenBy(e => e.Id)
            .ToArray();
        var items = due.Take(MaximumItems).Select(e => Classify(e, rows)).ToArray();
        var unknown =
            items.Count(i => i.Disposition == "unknown") + Math.Max(0, due.Length - items.Length);
        return new(
            due.Length,
            items.Count(i => i.Disposition == "required"),
            items.Count(i => i.Disposition == "redundant"),
            unknown,
            due.Length <= MaximumItems && unknown == 0,
            items
        );
    }

    private static HotelM2DueItem Classify(
        Envelope e,
        IReadOnlyDictionary<Guid, HotelM2QueueRow> rows
    )
    {
        var type = e.MessageType ?? "";
        byte[]? data;
        try
        {
            data = e.Data;
        }
        catch (WolverineSerializationException)
        {
            data = null;
        }
        var item = new HotelM2DueItem(
            e.Id,
            type.Length <= 256 ? type : "OversizedType",
            e.ScheduledTime ?? e.SentAt,
            data?.Length ?? 0,
            null,
            null,
            null,
            "unknown",
            "UnknownType"
        );
        if (e.Id == Guid.Empty || data is null || data.Length is 0 or > 2048)
            return item with { Reason = "InvalidEnvelope" };
        if (type == InboxType)
            return item with { Disposition = "required", Reason = "WebhookObservation" };
        if (type != WorkType)
            return item;
        WorkPayload? work;
        try
        {
            using var document = JsonDocument.Parse(data);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().Count() != 3
            )
                return item with { Reason = "InvalidWorkShape" };
            work = document.RootElement.Deserialize<WorkPayload>(Json);
        }
        catch (JsonException)
        {
            return item with { Reason = "InvalidWorkBody" };
        }
        if (work is null || work.ReservationId == Guid.Empty || work.DueAt == default)
            return item with { Reason = "InvalidWorkIdentity" };
        item = item with { ReservationId = work.ReservationId, Escalation = work.Escalation };
        if (!rows.TryGetValue(work.ReservationId, out var row))
            return item with { Reason = "MissingOwnedReservation" };
        item = item with { Outcome = row.Outcome.ToString() };
        if (
            !work.Escalation
            && row.HasAttempt
            && row.HasBookingId
            && row.Outcome is ReservationOutcome.Confirmed or ReservationOutcome.Cancelled
        )
            return item with { Disposition = "redundant", Reason = "TerminalReservation" };
        return item with
        {
            Disposition = "required",
            Reason = work.Escalation ? "Escalation" : "NonTerminalReservation",
        };
    }

    internal static bool Valid(HotelM2QueueSnapshot q) =>
        q.Complete
        && q.RawDue >= 0
        && q.RawDue <= MaximumItems
        && q.Items.Length == q.RawDue
        && q.UnknownDue == 0
        && q.RequiredDue == q.Items.Count(i => i.Disposition == "required")
        && q.RedundantDue == q.Items.Count(i => i.Disposition == "redundant")
        && q.RequiredDue + q.RedundantDue == q.RawDue;

    internal static bool Settled(HotelM2QueueSnapshot q, int pendingInbox, int rawDue) =>
        Valid(q) && q.RawDue == rawDue && q.RequiredDue == 0 && pendingInbox == 0;
}
