using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Hotels.Core;

public static class HotelErrors
{
    public static Error Invalid => Error.Validation("Hotels.Invalid", "Invalid stay request.");
    public static Error Expired =>
        Error.Conflict("Hotels.Expired", "Selection or quote expired. Search again.");
    public static Error Missing => Error.NotFound("Hotels.Missing", "Reservation not found.");
    public static Error Conflict =>
        Error.Conflict("Hotels.Conflict", "The purchase or revision differs.");
    public static Error Unavailable =>
        Error.Failure("Hotels.Unavailable", "Hotels is temporarily unavailable.");
    public static Error Capacity => Error.Failure("Hotels.Capacity", "Please try again later.");
    public static Error Unsupported =>
        Error.Validation("Hotels.Unsupported", "This stay cannot be purchased.");
}

public sealed record StayDates
{
    public DateOnly CheckIn { get; }
    public DateOnly CheckOut { get; }
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    [JsonConstructor]
    public StayDates(DateOnly checkIn, DateOnly checkOut)
    {
        if (checkOut <= checkIn)
            throw new ArgumentException("Invalid stay dates.");
        CheckIn = checkIn;
        CheckOut = checkOut;
    }

    public static ErrorOr<StayDates> Create(DateOnly checkIn, DateOnly checkOut) =>
        checkOut <= checkIn ? HotelErrors.Invalid : new StayDates(checkIn, checkOut);
}

public sealed record RoomOccupancy
{
    public int Adults { get; }

    [JsonConstructor]
    public RoomOccupancy(int adults)
    {
        if (adults is < 1 or > 2)
            throw new ArgumentException("Invalid occupancy.");
        Adults = adults;
    }

    public static ErrorOr<RoomOccupancy> Create(int adults, int rooms = 1, int children = 0) =>
        adults is < 1 or > 2 || rooms != 1 || children != 0
            ? HotelErrors.Invalid
            : new RoomOccupancy(adults);
}

public sealed record StayMoney
{
    [JsonNumberHandling(
        JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
    )]
    public decimal Amount { get; }
    public string Currency { get; }

    [JsonConstructor]
    public StayMoney(decimal amount, string currency)
    {
        if (amount < 0 || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Invalid money.");
        Amount = amount;
        Currency = currency;
    }

    public static ErrorOr<StayMoney> Create(decimal amount, string? currency) =>
        amount < 0
        || currency is null
        || currency.Length != 3
        || currency.Any(c => c is < 'A' or > 'Z')
            ? HotelErrors.Invalid
            : new StayMoney(amount, currency);
}

public sealed record HotelArea(
    string Key,
    decimal Latitude,
    decimal Longitude,
    int RadiusKm,
    bool Fictional = false
);

public sealed record StaySearch(HotelArea Area, StayDates Dates, RoomOccupancy Occupancy);

[JsonConverter(typeof(JsonStringEnumConverter<ExecutionKind>))]
public enum ExecutionKind
{
    Unsupported,
    Complete,
    Limited,
    Failed,
}

public sealed record CapabilitySupport(bool Supported, string? Reason)
{
    public static CapabilitySupport Yes { get; } = new(true, null);

    public static CapabilitySupport No(string reason) => new(false, reason);
}

public sealed record ProviderStatus(string Provider, ExecutionKind Kind, string? Reason);

public sealed record HotelExecution<T>(ExecutionKind Kind, T[] Items, string? Reason = null)
{
    public static HotelExecution<T> Unsupported(string reason) =>
        new(ExecutionKind.Unsupported, [], reason);

    public static HotelExecution<T> Failed(string code) => new(ExecutionKind.Failed, [], code);
}

public sealed record HotelProperty(
    string Id,
    string Name,
    string? Address,
    string? Rating,
    string? RatingSource
);

public sealed record ScopedFact(string Text, string Scope);

public sealed record CancellationTerms(string Kind, ScopedFact[] Rules);

public sealed record StayTerms(
    HotelProperty Property,
    string Room,
    string RateId,
    StayDates Dates,
    RoomOccupancy Occupancy,
    StayMoney Total,
    StayMoney[]? MandatoryLocalPayments,
    ScopedFact[] Board,
    CancellationTerms Cancellation,
    ScopedFact[] Conditions,
    bool Eligible,
    string PaymentTiming
);

public sealed record HotelOffer(
    string Provider,
    string Environment,
    string SelectionKey,
    StayTerms Terms,
    DateTimeOffset? SourceExpiresAt,
    bool Purchasable,
    string? PurchaseUnavailableReason
);

public sealed record SelectedStay(StaySearch Request, HotelOffer Offer);

public sealed record ProviderContextHandle(int Version, string Value)
{
    public override string ToString() => nameof(ProviderContextHandle);
}

public sealed record PreparedStay(
    StayTerms Terms,
    DateTimeOffset FreshUntil,
    ProviderContextHandle Context,
    DateTimeOffset? SourceExpiresAt = null,
    string? SourcePreparationReference = null,
    string AccountBinding = "configured-test-account"
);

public enum PreparationKind
{
    Ready,
    Unavailable,
    Unsupported,
    Failed,
}

public sealed record Preparation(
    PreparationKind Kind,
    PreparedStay? Stay = null,
    string? Reason = null
);

public sealed record LocalStayQuote(
    Guid ReservationId,
    int Revision,
    StayTerms Terms,
    DateTimeOffset ValidUntil
);

public sealed record HotelGuest(string GivenName, string FamilyName)
{
    public override string ToString() => nameof(HotelGuest);
}

public sealed record HotelParty(
    HotelGuest[] Guests,
    string Email,
    string Phone,
    bool AdultsConfirmed,
    bool CheckInConfirmed
)
{
    public override string ToString() => nameof(HotelParty);

    public bool IsValid(int adults) =>
        Guests is not null
        && Guests.Length == adults
        && Guests.All(g => g is not null && ValidName(g.GivenName) && ValidName(g.FamilyName))
        && Email is { Length: > 3 and <= 254 }
        && Email.Contains('@')
        && !Email.Any(char.IsControl)
        && Phone is { Length: >= 2 and <= 16 }
        && Phone[0] == '+'
        && Phone[1] is >= '1' and <= '9'
        && Phone.Skip(2).All(char.IsAsciiDigit)
        && AdultsConfirmed
        && CheckInConfirmed;

    private static bool ValidName(string? value) =>
        value is { Length: > 0 and <= 100 } && value == value.Trim() && !value.Any(char.IsControl);
}

public sealed record CreateStay(
    Guid ReservationId,
    Guid AttemptId,
    StayTerms Accepted,
    ProviderContextHandle Context,
    HotelParty Party
)
{
    public override string ToString() => nameof(CreateStay);
}

public enum ObservationKind
{
    Confirmed,
    ReservedWithoutDetails,
    Pending,
    NotCreated,
    Unknown,
    Cancelled,
}

public sealed record HotelBookingFacts(
    string BookingId,
    string? HotelReference,
    StayTerms? Actual,
    string? RequestReference = null
);

public sealed record BookingObservation(
    ObservationKind Kind,
    HotelBookingFacts? Facts = null,
    bool Attributable = true,
    bool SenderFinished = true,
    string? Diagnostic = null,
    bool CurrentRead = false,
    string? RequestReference = null
);

public interface IHotelSearchProvider
{
    string Key { get; }
    CapabilitySupport GetSupport(StaySearch request);
    Task<HotelExecution<HotelOffer>> SearchAsync(
        StaySearch request,
        CancellationToken cancellationToken
    );
    Task<HotelExecution<HotelOffer>> LoadRatesAsync(
        SelectedStay selection,
        CancellationToken cancellationToken
    );
}

public interface IHotelBookingProvider
{
    string Key { get; }
    CapabilitySupport GetSupport(SelectedStay selection);
    Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken cancellationToken);
    Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken cancellationToken);
}

public interface IHotelReservationReader
{
    Task<BookingObservation> ReadAsync(string bookingId, CancellationToken cancellationToken);
}
