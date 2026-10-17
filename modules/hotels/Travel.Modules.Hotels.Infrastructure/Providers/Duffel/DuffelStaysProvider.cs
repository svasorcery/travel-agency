using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Core;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

[TestOnly]
public sealed class DuffelStaysProvider(
    HttpClient http,
    IOptions<HotelsOptions> options,
    HotelCapacity capacity,
    TimeProvider time
) : IHotelSearchProvider, IHotelBookingProvider, IHotelReservationReader
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
    };
    public string Key => "duffel-stays";

    public CapabilitySupport GetSupport(StaySearch request)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return !Ready() ? CapabilitySupport.No("NotConfigured")
            : request.Area.Fictional ? CapabilitySupport.No("FictionalDestinationUnsupported")
            : request.Dates.CheckIn <= today
            || request.Dates.CheckIn > today.AddDays(330)
            || request.Dates.Nights is < 1 or > 99
            || request.Area.RadiusKm is < 1 or > 20
            || request.Area.Latitude is < -90 or > 90
            || request.Area.Longitude is < -180 or > 180
                ? CapabilitySupport.No("StayOutsideSourceBounds")
            : CapabilitySupport.Yes;
    }

    public CapabilitySupport GetSupport(SelectedStay selection)
    {
        var search = GetSupport(selection.Request);
        return !search.Supported ? search
            : selection.Offer.Provider != Key
            || selection.Offer.Environment != "Test"
            || !selection.Offer.Purchasable
                ? CapabilitySupport.No("PurchaseUnsupported")
            : CapabilitySupport.Yes;
    }

    public async Task<HotelExecution<HotelOffer>> SearchAsync(
        StaySearch request,
        CancellationToken cancellationToken
    )
    {
        var support = GetSupport(request);
        if (!support.Supported)
            return HotelExecution<HotelOffer>.Unsupported(support.Reason!);
        if (!Ready())
            return HotelExecution<HotelOffer>.Failed("NotConfigured");
        using var permit = capacity.TryAcquire(Key);
        if (permit is null)
            return HotelExecution<HotelOffer>.Failed("Capacity");
        try
        {
            var reply = await SendAsync(
                "stays/search",
                new
                {
                    rooms = 1,
                    check_in_date = request.Dates.CheckIn.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    ),
                    check_out_date = request.Dates.CheckOut.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    ),
                    guests = Enumerable
                        .Range(0, request.Occupancy.Adults)
                        .Select(_ => new { type = "adult" })
                        .ToArray(),
                    location = new
                    {
                        radius = request.Area.RadiusKm,
                        geographic_coordinates = new
                        {
                            latitude = request.Area.Latitude,
                            longitude = request.Area.Longitude,
                        },
                    },
                },
                false,
                cancellationToken
            );
            if (!reply.Success)
                return HotelExecution<HotelOffer>.Failed(
                    reply.Status == HttpStatusCode.TooManyRequests
                        ? "Capacity"
                        : "SupplierUnavailable"
                );
            var data =
                JsonSerializer.Deserialize<DuffelStaysEnvelope<DuffelStaysSearchData>>(
                    reply.Bytes,
                    Wire
                ) ?? throw new JsonException();
            if (data.Data?.Results is null || data.Data.Results.Any(r => r is null))
                throw new JsonException();
            var items = data
                .Data.Results.Take(options.Value.Properties)
                .Select(r => DuffelStaysMapper.Indicative(r, request))
                .ToArray();
            var limited =
                data.Data.Results.Length > options.Value.Properties || data.Meta?.After is not null;
            return new(
                limited ? ExecutionKind.Limited : ExecutionKind.Complete,
                items,
                limited ? "SourceResultLimit" : null
            );
        }
        catch (Exception ex) when (SafeFailure(ex))
        {
            return HotelExecution<HotelOffer>.Failed("InvalidOrUnavailableReply");
        }
    }

    public async Task<HotelExecution<HotelOffer>> LoadRatesAsync(
        SelectedStay selection,
        CancellationToken cancellationToken
    )
    {
        var support = GetSupport(selection.Request);
        if (!support.Supported)
            return HotelExecution<HotelOffer>.Unsupported(support.Reason!);
        if (!Ready())
            return HotelExecution<HotelOffer>.Failed("NotConfigured");
        if (
            !DuffelStaysMapper.PathReference(selection.Offer.SelectionKey)
            || selection.Offer.SourceExpiresAt <= time.GetUtcNow()
        )
            return HotelExecution<HotelOffer>.Failed("SelectionExpired");
        using var permit = capacity.TryAcquire(Key);
        if (permit is null)
            return HotelExecution<HotelOffer>.Failed("Capacity");
        try
        {
            var reply = await SendAsync(
                $"stays/search_results/{selection.Offer.SelectionKey}/actions/fetch_all_rates",
                null,
                false,
                cancellationToken
            );
            if (!reply.Success)
                return HotelExecution<HotelOffer>.Failed("SupplierUnavailable");
            var data =
                JsonSerializer.Deserialize<DuffelStaysEnvelope<DuffelStaysResult>>(
                    reply.Bytes,
                    Wire
                ) ?? throw new JsonException();
            var result = data.Data;
            if (result?.Accommodation is null)
                throw new JsonException();
            DuffelStaysMapper.ValidateScope(
                result.CheckInDate,
                result.CheckOutDate,
                result.Rooms,
                result.Guests,
                selection.Request
            );
            if (result.Accommodation.Id != selection.Offer.Terms.Property.Id)
                throw new FormatException();
            var rates = DuffelStaysMapper.Rates(result.Accommodation);
            var offers = rates
                .Take(options.Value.Rates)
                .Select(x =>
                    DuffelStaysMapper.Rate(
                        result.Accommodation,
                        x.Room,
                        x.Rate,
                        selection.Request.Dates,
                        selection.Request.Occupancy,
                        result.ExpiresAt
                    )
                )
                .ToArray();
            var limited = rates.Length > options.Value.Rates;
            return new(
                limited ? ExecutionKind.Limited : ExecutionKind.Complete,
                offers,
                limited ? "SourceRateLimit" : null
            );
        }
        catch (Exception ex) when (SafeFailure(ex))
        {
            return HotelExecution<HotelOffer>.Failed("InvalidOrUnavailableReply");
        }
    }

    public async Task<Preparation> PrepareAsync(
        SelectedStay selection,
        CancellationToken cancellationToken
    )
    {
        var support = GetSupport(selection);
        if (!support.Supported)
            return new(PreparationKind.Unsupported, Reason: support.Reason);
        if (!Ready() || !options.Value.BookingEnabled)
            return new(PreparationKind.Unavailable, Reason: "BookingDisabled");
        if (selection.Offer.SourceExpiresAt <= time.GetUtcNow())
            return new(PreparationKind.Unavailable, Reason: "SelectionExpired");
        using var permit = capacity.TryAcquire(Key);
        if (permit is null)
            return new(PreparationKind.Failed, Reason: "Capacity");
        try
        {
            var reply = await SendAsync(
                "stays/quotes",
                new { rate_id = selection.Offer.Terms.RateId },
                false,
                cancellationToken
            );
            if (!reply.Success)
                return new(PreparationKind.Failed, Reason: "SupplierUnavailable");
            var quote =
                JsonSerializer
                    .Deserialize<DuffelStaysEnvelope<DuffelStaysQuote>>(reply.Bytes, Wire)
                    ?.Data
                ?? throw new JsonException();
            DuffelStaysMapper.ValidateScope(
                quote.CheckInDate,
                quote.CheckOutDate,
                quote.Rooms,
                quote.Guests,
                selection.Request
            );
            if (
                quote.Accommodation is null
                || quote.Accommodation.Id != selection.Offer.Terms.Property.Id
            )
                throw new FormatException();
            var (room, rate) = DuffelStaysMapper.Rates(quote.Accommodation).Single();
            var offer = DuffelStaysMapper.Rate(
                quote.Accommodation,
                room,
                rate,
                selection.Request.Dates,
                selection.Request.Occupancy
            );
            var terms = offer.Terms with
            {
                Total = DuffelStaysMapper.Money(quote.TotalAmount, quote.TotalCurrency),
                MandatoryLocalPayments = quote.DueAtAccommodationAmount is null
                    ? null
                    :
                    [
                        DuffelStaysMapper.Money(
                            quote.DueAtAccommodationAmount,
                            quote.DueAtAccommodationCurrency
                        ),
                    ],
            };
            if (!offer.Purchasable || terms.MandatoryLocalPayments is null)
                return new(
                    PreparationKind.Unsupported,
                    Reason: offer.PurchaseUnavailableReason ?? "LocalAmountsUnknown"
                );
            var quoteId = DuffelStaysMapper.Reference(quote.Id);
            return new(
                PreparationKind.Ready,
                new(
                    terms,
                    time.GetUtcNow().AddMinutes(5),
                    new(1, JsonSerializer.Serialize(new DuffelStaysContext(quoteId, rate.Id))),
                    offer.SourceExpiresAt,
                    quoteId,
                    options.Value.OrganisationId ?? "configured-test-account"
                )
            );
        }
        catch (Exception ex) when (SafeFailure(ex))
        {
            return new(PreparationKind.Failed, Reason: "InvalidOrUnavailableReply");
        }
    }

    public async Task<BookingObservation> CreateAsync(
        CreateStay purchase,
        CancellationToken cancellationToken
    )
    {
        if (!Ready() || !options.Value.BookingEnabled)
            return new(ObservationKind.Unknown, Diagnostic: "BookingDisabled");
        try
        {
            var context =
                JsonSerializer.Deserialize<DuffelStaysContext>(purchase.Context.Value)
                ?? throw new JsonException();
            if (purchase.Context.Version != 1 || !DuffelStaysMapper.PathReference(context.QuoteId))
                throw new FormatException();
            var reply = await SendAsync(
                "stays/bookings",
                new
                {
                    quote_id = context.QuoteId,
                    guests = purchase
                        .Party.Guests.Select(g => new
                        {
                            given_name = g.GivenName,
                            family_name = g.FamilyName,
                        })
                        .ToArray(),
                    email = purchase.Party.Email,
                    phone_number = purchase.Party.Phone,
                    metadata = new
                    {
                        travel_reservation = purchase.ReservationId.ToString("N"),
                        travel_attempt = purchase.AttemptId.ToString("N"),
                    },
                },
                true,
                cancellationToken
            );
            if (reply.Status == HttpStatusCode.OK)
                return new(
                    ValidMessage(reply.Bytes)
                        ? ObservationKind.ReservedWithoutDetails
                        : ObservationKind.Unknown,
                    Diagnostic: "ReservedDetailsPending",
                    RequestReference: reply.RequestReference
                );
            if (reply.Status == HttpStatusCode.Accepted)
                return new(
                    ObservationKind.Pending,
                    Diagnostic: "SourcePending",
                    RequestReference: reply.RequestReference
                );
            if (
                reply.Status == HttpStatusCode.UnprocessableEntity
                && ExistingBookingOutcome(reply.Bytes) is { } existing
            )
                return new(
                    existing,
                    Diagnostic: existing == ObservationKind.Pending
                        ? "SourcePending"
                        : "ReservedDetailsPending",
                    RequestReference: reply.RequestReference
                );
            if (
                reply.Status == HttpStatusCode.ServiceUnavailable
                && PositiveNoCreation(reply.Bytes)
            )
                return new(
                    ObservationKind.NotCreated,
                    Diagnostic: "SourceNotCreated",
                    RequestReference: reply.RequestReference
                );
            if (reply.Status != HttpStatusCode.Created)
                return new(
                    ObservationKind.Unknown,
                    Diagnostic: "TransportUnknown",
                    RequestReference: reply.RequestReference
                );
            return DecodeBooking(reply.Bytes, reply.RequestReference);
        }
        catch (Exception ex) when (SafeFailure(ex))
        {
            return new(ObservationKind.Unknown, Diagnostic: "TransportUnknown");
        }
    }

    public async Task<BookingObservation> ReadAsync(
        string bookingId,
        CancellationToken cancellationToken
    )
    {
        if (!Ready() || !DuffelStaysMapper.PathReference(bookingId))
            return new(ObservationKind.Unknown, Attributable: false);
        try
        {
            var reply = await SendAsync(
                $"stays/bookings/{bookingId}",
                null,
                false,
                cancellationToken,
                get: true
            );
            if (!reply.Success)
                return new(ObservationKind.Unknown, Attributable: false);
            var outcome = DecodeBooking(reply.Bytes, reply.RequestReference);
            return outcome.Facts?.BookingId == bookingId
                ? outcome with
                {
                    CurrentRead = true,
                }
                : new(ObservationKind.Unknown, Attributable: false);
        }
        catch (Exception ex) when (SafeFailure(ex))
        {
            return new(ObservationKind.Unknown, Attributable: false);
        }
    }

    internal static BookingObservation DecodeBooking(byte[] bytes, string? requestReference)
    {
        var booking =
            JsonSerializer.Deserialize<DuffelStaysEnvelope<DuffelStaysBooking>>(bytes, Wire)?.Data
            ?? throw new JsonException();
        if (booking.Status is not ("confirmed" or "cancelled"))
            throw new FormatException();
        var id = DuffelStaysMapper.Reference(booking.Id);
        if (
            booking.Rooms != 1
            || booking.GuestTypes is { } guests
                && (guests.Length is < 1 or > 2 || guests.Any(g => g is null || g.Type != "adult"))
        )
            throw new FormatException();
        var dates = DuffelStaysMapper.Dates(booking.CheckInDate, booking.CheckOutDate);
        var (room, rate) = DuffelStaysMapper.Rates(booking.Accommodation).Single();
        var reference = booking.Reference is null
            ? null
            : DuffelStaysMapper.Text(booking.Reference);
        StayTerms? terms = null;
        if (booking.GuestTypes is null)
            DuffelStaysMapper.ValidateRateFacts(booking.Accommodation, room, rate, dates);
        else
            terms = DuffelStaysMapper
                .Rate(booking.Accommodation, room, rate, dates, new(booking.GuestTypes.Length))
                .Terms;
        return new(
            booking.Status == "cancelled" ? ObservationKind.Cancelled
                : terms is not null ? ObservationKind.Confirmed
                : ObservationKind.ReservedWithoutDetails,
            new(id, reference, terms, requestReference),
            RequestReference: requestReference
        );
    }

    private bool Ready() => options.Value.Enabled && options.Value.IsValid(false);

    private static bool PositiveNoCreation(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        return root.TryGetProperty("meta", out var meta)
            && meta.TryGetProperty("status", out var status)
            && status.GetInt32() == 503
            && meta.TryGetProperty("request_id", out var id)
            && ReservationState.SafeReference(id.GetString())
            && root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array
            && errors.GetArrayLength() > 0
            && errors
                .EnumerateArray()
                .All(e =>
                    e.TryGetProperty("type", out var type)
                    && type.GetString() == "api_error"
                    && e.TryGetProperty("code", out var code)
                    && code.ValueKind == JsonValueKind.String
                    && e.TryGetProperty("title", out var title)
                    && title.ValueKind == JsonValueKind.String
                    && e.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && e.TryGetProperty("documentation_url", out var url)
                    && url.GetString() == "https://duffel.com/docs/api/overview/response-handling"
                );
    }

    private static ObservationKind? ExistingBookingOutcome(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        if (
            json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("errors", out var errors)
            || errors.ValueKind != JsonValueKind.Array
            || errors.GetArrayLength() != 1
        )
            return null;
        var error = errors[0];
        if (
            error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("type", out var type)
            || type.GetString() != "invalid_state_error"
            || !error.TryGetProperty("code", out var code)
        )
            return null;
        return code.GetString() switch
        {
            "booking_already_attempted" => ObservationKind.Pending,
            "booking_already_confirmed" => ObservationKind.ReservedWithoutDetails,
            _ => null,
        };
    }

    private static bool ValidMessage(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(message.GetString());
    }

    private sealed record Reply(HttpStatusCode Status, byte[] Bytes, string? RequestReference)
    {
        public bool Success => (int)Status is >= 200 and < 300;
    }

    private async Task<Reply> SendAsync(
        string path,
        object? body,
        bool create,
        CancellationToken ct,
        bool get = false
    )
    {
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(
                create ? options.Value.CreateTimeoutSeconds : options.Value.ReadTimeoutSeconds
            ),
            time
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var request = new HttpRequestMessage(
            get ? HttpMethod.Get : HttpMethod.Post,
            "https://api.duffel.com/" + path
        );
        request.Headers.Authorization = new("Bearer", options.Value.ApiKey);
        request.Headers.Add("Duffel-Version", "v2");
        request.Headers.Accept.Add(new("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(new { data = body });
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Supplier redirect refused.");
        if (response.Headers.RetryAfter?.Delta is { } delta)
            capacity.TightenUntil(time.GetUtcNow().Add(delta));
        if (response.Headers.RetryAfter?.Date is { } date)
            capacity.TightenUntil(date);
        if (response.Content.Headers.ContentLength > options.Value.ResponseBytes)
            throw new FormatException();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) != 0)
        {
            if (buffer.Length + count > options.Value.ResponseBytes)
                throw new FormatException();
            buffer.Write(chunk, 0, count);
        }
        var bytes = buffer.ToArray();
        var reference = response.Headers.TryGetValues("x-request-id", out var refs)
            ? refs.SingleOrDefault()
            : null;
        try
        {
            using var json = JsonDocument.Parse(bytes);
            if (
                json.RootElement.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("request_id", out var requestId)
                && requestId.ValueKind == JsonValueKind.String
            )
                reference = requestId.GetString();
        }
        catch (JsonException)
        { /* Parser at the operation boundary reports malformed input. */
        }
        if (
            response.Headers.TryGetValues("ratelimit-remaining", out var remaining)
            && remaining.SingleOrDefault() == "0"
            && response.Headers.TryGetValues("ratelimit-reset", out var resets)
            && DateTimeOffset.TryParse(
                resets.SingleOrDefault(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var reset
            )
        )
            capacity.TightenUntil(reset);
        return new(
            response.StatusCode,
            bytes,
            ReservationState.SafeReference(reference) ? reference : null
        );
    }

    private static bool SafeFailure(Exception ex) =>
        ex
            is JsonException
                or FormatException
                or ArgumentException
                or InvalidOperationException
                or HttpRequestException
                or OperationCanceledException
                or IOException;
}
