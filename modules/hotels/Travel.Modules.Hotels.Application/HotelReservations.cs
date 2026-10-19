using ErrorOr;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application;

public sealed class HotelReservation
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public int Version { get; set; }
    public int QuoteRevision { get; set; }
    public string Provider { get; set; } = "";
    public string Environment { get; set; } = "Test";
    public string ProviderAccount { get; set; } = "configured-test-account";
    public string? SourcePreparationReference { get; set; }
    public string? ProviderRequestReference { get; set; }
    public string? OutcomeDiagnostic { get; set; }
    public StayTerms? Terms { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? ActionId { get; set; }
    public DateTimeOffset? AdmittedAt { get; set; }
    public bool SenderFinished { get; set; }
    public string? ProtectedContext { get; set; }
    public string? ProtectedParty { get; set; }
    public DateTimeOffset? PrivatePurgeAt { get; set; }
    public ReservationState State { get; set; } = new();
}

public sealed record HotelPrivacyBinding(
    Guid OwnerId,
    Guid ReservationId,
    int QuoteRevision,
    int Guests,
    string Kind
);

public interface IHotelPrivateProtector
{
    ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding);
    ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding);
}

public interface IHotelCapacity
{
    IDisposable? TryAcquire(string provider, bool recovery = false);
}

public sealed record HotelWork(Guid ReservationId, bool Escalation, DateTimeOffset DueAt);

public interface IHotelReservationStore
{
    Task<ErrorOr<HotelReservation?>> LoadAsync(Guid id, Guid? owner, CancellationToken ct);
    Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct);

    // Commit the row and any work atomically; false means another revision won.
    Task<ErrorOr<bool>> SaveAsync(
        HotelReservation reservation,
        int expectedVersion,
        HotelWork[] work,
        CancellationToken ct
    );
}

public sealed record HotelPurchase(Guid ActionId, int QuoteRevision, bool Consent, HotelParty Party)
{
    public override string ToString() => nameof(HotelPurchase);
}

public sealed record HotelReservationView(
    Guid Id,
    int QuoteRevision,
    StayTerms? Accepted,
    DateTimeOffset ValidUntil,
    Guid? AttemptId,
    ReservationOutcome Outcome,
    ReviewReason[] ReviewReasons,
    bool EverReserved,
    bool Active,
    HotelBookingFacts? Facts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool PrivateContentAvailable,
    HotelTermsMismatch? FirstMismatch = null
)
{
    public string Provider { get; init; } = "";
    public string Environment { get; init; } = "";
    public HotelSourceIdentity? Source { get; init; }
    public HotelPriceEvidence? AcceptedPrice { get; init; }
    public HotelConditionEvidence? AcceptedConditions { get; init; }

    public static HotelReservationView From(HotelReservation r, DateTimeOffset now) =>
        new(
            r.Id,
            r.QuoteRevision,
            r.PrivatePurgeAt <= now ? null : r.Terms,
            r.ValidUntil,
            r.AttemptId,
            r.State.Outcome,
            r.State.Review.Order().ToArray(),
            r.State.EverReserved,
            r.State.Active,
            r.PrivatePurgeAt <= now || r.Terms is null || r.State.Facts is null
                ? null
                : r.State.Facts with
                {
                    RequestReference = null,
                },
            r.CreatedAt,
            r.UpdatedAt,
            (r.PrivatePurgeAt is null || r.PrivatePurgeAt > now) && r.ProtectedContext is not null,
            r.PrivatePurgeAt <= now || r.Terms is null ? null : r.State.FirstMismatch
        )
        {
            Provider = r.Provider,
            Environment = r.Environment,
            Source = r.State.AdmissionBinding?.Source,
            AcceptedPrice = r.PrivatePurgeAt <= now ? null : r.State.AcceptedPrice,
            AcceptedConditions = r.PrivatePurgeAt <= now ? null : r.State.AcceptedConditions,
        };
}
