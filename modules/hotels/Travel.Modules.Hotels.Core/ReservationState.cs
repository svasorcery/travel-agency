using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Hotels.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ReservationOutcome>))]
public enum ReservationOutcome
{
    Quoted,
    Submitted,
    Pending,
    Unknown,
    ReservedAwaitingDetails,
    Confirmed,
    NotCreated,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<ReviewReason>))]
public enum ReviewReason
{
    Deadline,
    MissingDetails,
    TransportUnknown,
    TermsMismatch,
    Conflict,
    ObservationUnavailable,
}

public sealed class ReservationState
{
    public ReservationOutcome Outcome { get; set; } = ReservationOutcome.Quoted;
    public HashSet<ReviewReason> Review { get; set; } = [];
    public bool EverReserved { get; set; }
    public bool NoCreationObserved { get; set; }
    public bool Active =>
        Outcome is ReservationOutcome.Confirmed or ReservationOutcome.ReservedAwaitingDetails;
    public HotelBookingFacts? Facts { get; set; }
    public string? BoundBookingId { get; set; }
    public DateTimeOffset? FirstKnownAt { get; set; }
    public DateTimeOffset? ReadUntil { get; set; }
    public int ReadsClaimed { get; set; }
    public int ReadAllowance { get; set; } = 3;
    public int ReadWindowSeconds { get; set; } = 300;
    public int SecondReadDelaySeconds { get; set; } = 30;
    public int ThirdReadDelaySeconds { get; set; } = 120;
    public int FinishedRetentionDays { get; set; } = 90;
    public DateTimeOffset? NextReadAt { get; set; }
    public DateTimeOffset? EscalateAt { get; set; }

    public bool ClaimRead(DateTimeOffset now)
    {
        if (
            BoundBookingId is null
            || ReadUntil is null
            || now >= ReadUntil
            || ReadsClaimed >= ReadAllowance
            || NextReadAt > now
        )
            return false;
        ReadsClaimed++;
        NextReadAt = now.AddSeconds(
            ReadsClaimed == 1 ? SecondReadDelaySeconds : ThirdReadDelaySeconds
        );
        return true;
    }

    public void Escalate(DateTimeOffset now)
    {
        if (
            EscalateAt <= now
            && Outcome
                is ReservationOutcome.Submitted
                    or ReservationOutcome.Pending
                    or ReservationOutcome.Unknown
                    or ReservationOutcome.ReservedAwaitingDetails
        )
            Review.Add(ReviewReason.Deadline);
    }

    public void Apply(BookingObservation observation, StayTerms? accepted, DateTimeOffset now)
    {
        if (!observation.Attributable)
            return;
        var incoming = observation.Facts;
        if (incoming is not null)
        {
            if (!SafeReference(incoming.BookingId))
            {
                Review.Add(ReviewReason.Conflict);
                return;
            }
            if (BoundBookingId is not null && BoundBookingId != incoming.BookingId)
            {
                Review.Add(ReviewReason.Conflict);
                return;
            }
            if (BoundBookingId is null)
            {
                BoundBookingId = incoming.BookingId;
                FirstKnownAt = now;
                ReadUntil = now.AddSeconds(ReadWindowSeconds);
                NextReadAt = now;
            }
        }
        var positive =
            observation.Kind
            is ObservationKind.Confirmed
                or ObservationKind.ReservedWithoutDetails
                or ObservationKind.Cancelled;
        if (
            positive
            && incoming is null
            && observation.Kind != ObservationKind.ReservedWithoutDetails
        )
        {
            Review.Add(ReviewReason.Conflict);
            return;
        }
        if (positive)
        {
            if (Outcome == ReservationOutcome.NotCreated || NoCreationObserved)
                Review.Add(ReviewReason.Conflict);
            EverReserved = true;
            if (incoming is not null)
                Facts = new(
                    incoming.BookingId,
                    incoming.HotelReference ?? Facts?.HotelReference,
                    incoming.Actual ?? Facts?.Actual,
                    incoming.RequestReference ?? Facts?.RequestReference
                );
        }
        switch (observation.Kind)
        {
            case ObservationKind.Confirmed:
                if (Outcome == ReservationOutcome.Cancelled && observation.CurrentRead)
                    Review.Add(ReviewReason.Conflict);
                if (incoming!.Actual is null)
                {
                    if (
                        Outcome != ReservationOutcome.Cancelled
                        && Outcome != ReservationOutcome.Confirmed
                    )
                        Outcome = ReservationOutcome.ReservedAwaitingDetails;
                    Review.Add(ReviewReason.MissingDetails);
                    return;
                }
                if (accepted is not null && !TermsMatch(accepted, incoming.Actual))
                    Review.Add(ReviewReason.TermsMismatch);
                if (Outcome != ReservationOutcome.Cancelled)
                    Outcome = ReservationOutcome.Confirmed;
                ClearWaiting();
                break;
            case ObservationKind.Cancelled:
                Outcome = ReservationOutcome.Cancelled;
                if (Facts?.Actual is not null)
                    ClearWaiting();
                else
                    Review.Add(ReviewReason.MissingDetails);
                break;
            case ObservationKind.ReservedWithoutDetails:
                if (Outcome == ReservationOutcome.Cancelled && observation.CurrentRead)
                    Review.Add(ReviewReason.Conflict);
                if (Outcome is not (ReservationOutcome.Cancelled or ReservationOutcome.Confirmed))
                {
                    Outcome = ReservationOutcome.ReservedAwaitingDetails;
                    Review.Add(ReviewReason.MissingDetails);
                }
                break;
            case ObservationKind.NotCreated:
                NoCreationObserved = true;
                if (EverReserved)
                    Review.Add(ReviewReason.Conflict);
                else if (observation.SenderFinished)
                {
                    Outcome = ReservationOutcome.NotCreated;
                    ClearWaiting();
                }
                break;
            case ObservationKind.Pending:
                if (!EverReserved && Outcome != ReservationOutcome.NotCreated)
                    Outcome = ReservationOutcome.Pending;
                break;
            case ObservationKind.Unknown:
                if (!EverReserved && Outcome != ReservationOutcome.NotCreated)
                {
                    Outcome = ReservationOutcome.Unknown;
                    Review.Add(ReviewReason.TransportUnknown);
                }
                break;
        }
        if (NoCreationObserved && observation.SenderFinished && !EverReserved)
        {
            Outcome = ReservationOutcome.NotCreated;
            ClearWaiting();
        }
    }

    private void ClearWaiting()
    {
        Review.Remove(ReviewReason.Deadline);
        Review.Remove(ReviewReason.MissingDetails);
        Review.Remove(ReviewReason.TransportUnknown);
        Review.Remove(ReviewReason.ObservationUnavailable);
    }

    public static bool TermsMatch(StayTerms left, StayTerms right) =>
        left.Property.Id == right.Property.Id
        && left.Room == right.Room
        && left.Dates == right.Dates
        && left.Occupancy == right.Occupancy
        && left.Total == right.Total
        && Same(left.MandatoryLocalPayments, right.MandatoryLocalPayments)
        && Same(left.Board, right.Board)
        && Same(left.Conditions, right.Conditions)
        && left.Cancellation.Kind == right.Cancellation.Kind
        && Same(left.Cancellation.Rules, right.Cancellation.Rules)
        && left.Eligible == right.Eligible
        && left.PaymentTiming == right.PaymentTiming;

    private static bool Same<T>(T[]? left, T[]? right) =>
        left is null || right is null
            ? left is null && right is null
            : left.Select(x => JsonSerializer.Serialize(x))
                .Order(StringComparer.Ordinal)
                .SequenceEqual(
                    right.Select(x => JsonSerializer.Serialize(x)).Order(StringComparer.Ordinal)
                );

    public static bool SafeReference(string? value) =>
        value is { Length: > 0 and <= 160 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');
}
