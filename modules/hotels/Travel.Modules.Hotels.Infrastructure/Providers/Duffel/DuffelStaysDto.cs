namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

internal sealed record DuffelStaysEnvelope<T>(T Data, DuffelStaysMeta? Meta = null);

internal sealed record DuffelStaysMeta(
    string? After = null,
    string? RequestId = null,
    int? Status = null
);

internal sealed record DuffelStaysSearchData(DuffelStaysResult[] Results);

internal sealed record DuffelStaysResult(
    string Id,
    string CheckInDate,
    string CheckOutDate,
    int Rooms,
    DuffelStaysGuestType[]? Guests,
    string CheapestRateTotalAmount,
    string CheapestRateCurrency,
    DateTimeOffset? ExpiresAt,
    DuffelStaysAccommodation Accommodation
);

internal sealed record DuffelStaysGuestType(string Type);

internal sealed record DuffelStaysAccommodation(
    string Id,
    string Name,
    DuffelStaysRoom[]? Rooms,
    decimal? Rating,
    DuffelStaysLocation? Location,
    DuffelStaysCheckIn? CheckInInformation
);

internal sealed record DuffelStaysLocation(DuffelStaysAddress? Address);

internal sealed record DuffelStaysAddress(string? LineOne, string? CityName, string? CountryCode);

internal sealed record DuffelStaysCheckIn(
    string? CheckInAfterTime,
    string? CheckInBeforeTime,
    string? CheckOutBeforeTime
);

internal sealed record DuffelStaysRoom(string Name, DuffelStaysRate[] Rates);

internal sealed record DuffelStaysRate(
    string Id,
    string TotalAmount,
    string TotalCurrency,
    string? DueAtAccommodationAmount,
    string? DueAtAccommodationCurrency,
    string? PaymentType,
    string[][]? AvailablePaymentMethods,
    bool? LoyaltyProgrammeRequired,
    string[]? DealTypes,
    string? BoardType,
    DuffelStaysCondition[]? Conditions,
    DuffelStaysCancellation[]? CancellationTimeline,
    DateTimeOffset? ExpiresAt,
    DuffelStaysBenefit[]? Benefits = null
);

internal sealed record DuffelStaysCondition(string? Title, string? Description);

internal sealed record DuffelStaysBenefit(string? Type, string? Title, string? Description);

internal sealed record DuffelStaysCancellation(
    string RefundAmount,
    string Currency,
    DateTimeOffset? Before
);

internal sealed record DuffelStaysQuote(
    string Id,
    string CheckInDate,
    string CheckOutDate,
    int Rooms,
    DuffelStaysGuestType[]? Guests,
    string TotalAmount,
    string TotalCurrency,
    string? DueAtAccommodationAmount,
    string? DueAtAccommodationCurrency,
    DuffelStaysAccommodation Accommodation
);

internal sealed record DuffelStaysBooking(
    string Id,
    string? Reference,
    string? Status,
    string CheckInDate,
    string CheckOutDate,
    int Rooms,
    [property: System.Text.Json.Serialization.JsonRequired] DuffelStaysGuestType[]? GuestTypes,
    DuffelStaysAccommodation Accommodation
);

internal sealed record DuffelStaysContext(string QuoteId, string RateId);
