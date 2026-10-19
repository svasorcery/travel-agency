using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

[TestOnly]
public sealed class HotelbedsStayProvider(
    HttpClient http,
    IOptions<HotelbedsOptions> options,
    HotelCapacity capacity,
    TimeProvider time,
    IOptions<HotelsOptions>? moduleOptions = null
)
    : IHotelSearchProvider,
        IHotelBookingProvider,
        IHotelReservationReader,
        IHotelSourceDescriptor,
        IHotelPartySupport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 32,
    };
    public string Key => "hotelbeds";
    private string CredentialIdentity =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ApiKey ?? "")))
            .ToLowerInvariant();
    private bool RightsCleared =>
        options.Value.Fictional
        || options.Value.ExternalUseApproved
            && moduleOptions?.Value.Aggregation is { Enabled: true, RightsCleared: true };

    public CapabilitySupport GetPartySupport(HotelParty party) =>
        party.Guests.Any(g => g.GivenName.Length > 50 || g.FamilyName.Length > 50)
            ? CapabilitySupport.No("GuestNameUnsupported")
            : CapabilitySupport.Yes;

    public HotelSourceDescriptor CurrentSource =>
        new(
            new(
                Key,
                options.Value.AccountAlias,
                "Test",
                options.Value.Fictional ? "Fictional" : "External"
            ),
            options.Value.ConfigurationRevision,
            options.Value.PricingProfile,
            options.Value.ApprovalRevision,
            options.Value.Enabled
                && options.Value.BookingEnabled
                && RightsCleared
                && (
                    moduleOptions is null
                    || moduleOptions.Value.Enabled
                        && moduleOptions.Value.BookingEnabled
                        && moduleOptions.Value.Aggregation.Enabled
                )
                && options.Value.Fictional == (http is HotelbedsFictionalHttpClient),
            CredentialIdentity,
            CredentialIdentity
        );

    public CapabilitySupport GetSupport(StaySearch request)
    {
        var o = options.Value;
        if (!o.Enabled)
            return CapabilitySupport.No("Disabled");
        if (!RightsCleared)
            return CapabilitySupport.No("RightsNotCleared");
        if (moduleOptions is not null && !moduleOptions.Value.Aggregation.Enabled)
            return CapabilitySupport.No("AggregationDisabled");
        if (!o.IsValid(false) || o.Fictional != (http is HotelbedsFictionalHttpClient))
            return CapabilitySupport.No("NotConfigured");
        if (request.Area.Fictional != o.Fictional)
            return CapabilitySupport.No("DestinationUnsupported");
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return
            request.Dates.CheckIn <= today
            || request.Dates.CheckIn > today.AddDays(330)
            || request.Dates.Nights is < 1 or > 99
            ? CapabilitySupport.No("StayOutsideSourceBounds")
            : CapabilitySupport.Yes;
    }

    public CapabilitySupport GetSupport(SelectedStay selection) =>
        selection.Offer.Provider != Key
        || selection.Offer.Environment != "Test"
        || selection.Offer.Source != CurrentSource.Identity
            ? CapabilitySupport.No("SourceMismatch")
            : GetSupport(selection.Request);

    public async Task<HotelExecution<HotelOffer>> SearchAsync(
        StaySearch request,
        CancellationToken ct
    )
    {
        var support = GetSupport(request);
        if (!support.Supported)
            return HotelExecution<HotelOffer>.Unsupported(support.Reason!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var lease = capacity.TryAcquire(Key);
        if (lease is null)
            return new(ExecutionKind.Failed, [], "LocalCapacity", false);
        try
        {
            using var reply = await Send(
                "hotels",
                new
                {
                    stay = new
                    {
                        checkIn = Date(request.Dates.CheckIn),
                        checkOut = Date(request.Dates.CheckOut),
                    },
                    occupancies = new[]
                    {
                        new
                        {
                            rooms = 1,
                            adults = request.Occupancy.Adults,
                            children = 0,
                        },
                    },
                    hotels = options.Value.Fictional ? new { hotel = new[] { 900001 } } : null,
                    geolocation = options.Value.Fictional
                        ? null
                        : (object)
                            new
                            {
                                latitude = request.Area.Latitude.ToString(
                                    CultureInfo.InvariantCulture
                                ),
                                longitude = request.Area.Longitude.ToString(
                                    CultureInfo.InvariantCulture
                                ),
                                radius = request.Area.RadiusKm.ToString(
                                    CultureInfo.InvariantCulture
                                ),
                                unit = "km",
                            },
                },
                linked.Token
            );
            var root = reply.RootElement.GetProperty("hotels");
            ValidateDates(root, request.Dates);
            var hotels = root.GetProperty("hotels").EnumerateArray().ToArray();
            var candidates =
                new List<(JsonElement Hotel, JsonElement Room, JsonElement Rate, string Key)>();
            var reason = (string?)null;
            foreach (var hotel in hotels.OrderBy(h => h.GetProperty("code").GetInt32()).Take(100))
            foreach (var room in hotel.GetProperty("rooms").EnumerateArray())
            foreach (var rate in room.GetProperty("rates").EnumerateArray())
            {
                var key = rate.GetProperty("rateKey").GetString() ?? throw new JsonException();
                if (Encoding.UTF8.GetByteCount(key) > 4096)
                {
                    reason = "ContextTooLarge";
                    continue;
                }
                var semantic = Map(hotel, room, rate, request);
                if (Encoding.UTF8.GetByteCount(semantic.SelectionContext!.Value) > 4096)
                {
                    reason = "ContextTooLarge";
                    continue;
                }
                candidates.Add((hotel, room, rate, HotelCostRanking.SemanticKey(semantic)));
            }
            if (candidates.Count > 200)
                reason ??= "SourceRateLimit";
            if (hotels.Length > 100)
                reason ??= "SourceResultLimit";
            var offers = candidates
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Take(200)
                .Select(c => Map(c.Hotel, c.Room, c.Rate, request))
                .ToArray();
            linked.Token.ThrowIfCancellationRequested();
            return new(
                reason is null ? ExecutionKind.Complete : ExecutionKind.Limited,
                offers,
                reason
            );
        }
        catch (Exception ex) when (Safe(ex))
        {
            return Failure(ex, ct);
        }
    }

    public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
        SelectedStay selection,
        CancellationToken ct
    )
    {
        var support = GetSupport(selection);
        if (!support.Supported)
            return Task.FromResult(HotelExecution<HotelOffer>.Unsupported(support.Reason!));
        // Availability already includes complete rate variants. Refresh through the same owned source
        // instead of inventing a rates endpoint or reviving an expired handle.
        return Rates(selection, ct);
    }

    private async Task<HotelExecution<HotelOffer>> Rates(
        SelectedStay selection,
        CancellationToken ct
    )
    {
        var result = await SearchAsync(selection.Request, ct);
        return result with
        {
            Items = result
                .Items.Where(o => o.Terms.Property.Id == selection.Offer.Terms.Property.Id)
                .ToArray(),
        };
    }

    public async Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken ct)
    {
        var support = GetSupport(selection);
        if (!support.Supported)
            return new(PreparationKind.Unsupported, Reason: support.Reason);
        if (!CurrentSource.NewPurchaseAllowed)
            return new(PreparationKind.Unavailable, Reason: "BookingDisabled");
        if (
            selection.Offer.SelectionContext is not { Version: 2 } handle
            || selection.Offer.SourceExpiresAt <= time.GetUtcNow()
        )
            return new(PreparationKind.Unavailable, Reason: "SelectionExpired");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var lease = capacity.TryAcquire(Key);
        if (lease is null)
            return new(PreparationKind.Failed, Reason: "LocalCapacity");
        try
        {
            var context = Context(handle);
            using var reply = await Send(
                "checkrates",
                new { rooms = new[] { new { rateKey = context.RateKey } } },
                linked.Token
            );
            var hotel = reply.RootElement.GetProperty("hotel");
            ValidateDates(hotel, selection.Request.Dates);
            if (
                hotel.GetProperty("code").GetInt32().ToString(CultureInfo.InvariantCulture)
                != selection.Offer.Terms.Property.Id
            )
                throw new JsonException();
            var room = hotel.GetProperty("rooms").EnumerateArray().Single();
            var rate = room.GetProperty("rates").EnumerateArray().Single();
            var fresh = Map(hotel, room, rate, selection.Request);
            if (
                room.GetProperty("code").GetString() != selection.Offer.RoomIdentity
                || Text(rate, "rateType", 20) != "BOOKABLE"
            )
                return new(PreparationKind.Unsupported, Reason: "RateNotBookable");
            if (!fresh.Purchasable)
                return new(PreparationKind.Unsupported, Reason: fresh.PurchaseUnavailableReason);
            var prepared = new PreparedStay(
                fresh.Terms,
                time.GetUtcNow().AddMinutes(5),
                fresh.SelectionContext!,
                fresh.SourceExpiresAt,
                null,
                CurrentSource.TransportAccountBinding!,
                fresh.AdmissionBinding,
                fresh.Price,
                fresh.ConditionEvidence
            );
            linked.Token.ThrowIfCancellationRequested();
            return new(PreparationKind.Ready, prepared);
        }
        catch (Exception ex) when (Safe(ex))
        {
            return new(PreparationKind.Failed, Reason: Reason(ex));
        }
    }

    public async Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken ct)
    {
        if (!CurrentSource.NewPurchaseAllowed || !options.Value.IsValid(false))
            return new(ObservationKind.Unknown, Diagnostic: "BookingDisabled");
        try
        {
            var context = Context(purchase.Context);
            if (
                context.Source != CurrentSource.Identity
                || purchase.Party.Guests.Any(g =>
                    g.GivenName.Length > 50 || g.FamilyName.Length > 50
                )
            )
                return new(ObservationKind.Unknown, Diagnostic: "TransportUnknown");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var lead = purchase.Party.Guests[0];
            using var reply = await Send(
                "bookings",
                new
                {
                    holder = new { name = lead.GivenName, surname = lead.FamilyName },
                    rooms = new[]
                    {
                        new
                        {
                            rateKey = context.RateKey,
                            paxes = purchase
                                .Party.Guests.Select(g => new
                                {
                                    roomId = 1,
                                    type = "AD",
                                    name = g.GivenName,
                                    surname = g.FamilyName,
                                })
                                .ToArray(),
                        },
                    },
                    clientReference = purchase.AttemptId.ToString("N")[..20],
                },
                linked.Token
            );
            var booking = reply.RootElement.GetProperty("booking");
            if (
                booking.TryGetProperty("clientReference", out var echo)
                && echo.GetString() != purchase.AttemptId.ToString("N")[..20]
            )
                return new(
                    ObservationKind.Unknown,
                    Attributable: false,
                    Diagnostic: "TransportUnknown"
                );
            return DecodeBooking(reply.RootElement, purchase.Accepted);
        }
        catch (Exception ex) when (Safe(ex))
        {
            return new(ObservationKind.Unknown, Diagnostic: "TransportUnknown");
        }
    }

    public async Task<BookingObservation> ReadAsync(string bookingId, CancellationToken ct)
    {
        if (!ReservationState.SafeReference(bookingId))
            return new(ObservationKind.Unknown, Attributable: false);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            using var reply = await Send(
                "bookings/" + Uri.EscapeDataString(bookingId),
                null,
                linked.Token
            );
            var observation = DecodeBooking(reply.RootElement, null);
            return observation.Facts?.BookingId == bookingId
                ? observation with
                {
                    CurrentRead = true,
                }
                : new(ObservationKind.Unknown, Attributable: false);
        }
        catch (Exception ex) when (Safe(ex))
        {
            return new(ObservationKind.Unknown, Attributable: false);
        }
    }

    private BookingObservation DecodeBooking(JsonElement root, StayTerms? accepted)
    {
        var booking = root.GetProperty("booking");
        var reference = Text(booking, "reference", 160);
        if (!ReservationState.SafeReference(reference))
            throw new JsonException();
        var status = Text(booking, "status", 20);
        if (status is not ("CONFIRMED" or "CANCELLED"))
            return new(ObservationKind.Unknown, Attributable: false);
        StayTerms? actual = null;
        if (booking.TryGetProperty("hotel", out var hotel))
        {
            try
            {
                var room = hotel.GetProperty("rooms").EnumerateArray().Single();
                var rate = room.GetProperty("rates").EnumerateArray().Single();
                var dates = new StayDates(
                    DateOnly.Parse(Text(hotel, "checkIn", 10), CultureInfo.InvariantCulture),
                    DateOnly.Parse(Text(hotel, "checkOut", 10), CultureInfo.InvariantCulture)
                );
                var search = new StaySearch(
                    options.Value.Fictional
                        ? Application.HotelSearchService.Areas[3]
                        : Application.HotelSearchService.Areas[0],
                    dates,
                    new(rate.GetProperty("adults").GetInt32())
                );
                actual = Map(hotel, room, rate, search).Terms with
                {
                    Total = Money(booking.GetProperty("totalNet"), Text(booking, "currency", 3)),
                };
                // Stage rate IDs are not purchase authority; property/room/scope/material facts are.
                if (accepted is not null)
                    actual = actual with { RateId = accepted.RateId };
            }
            catch (Exception ex) when (Safe(ex))
            {
                actual = null;
            }
        }
        return new(
            status == "CANCELLED" ? ObservationKind.Cancelled
                : actual is null ? ObservationKind.ReservedWithoutDetails
                : ObservationKind.Confirmed,
            new(reference, null, actual),
            Diagnostic: actual is null ? "ReservedDetailsPending" : null
        );
    }

    private HotelOffer Map(JsonElement hotel, JsonElement room, JsonElement rate, StaySearch search)
    {
        if (
            rate.GetProperty("rooms").GetInt32() != 1
            || rate.GetProperty("adults").GetInt32() != search.Occupancy.Adults
            || rate.GetProperty("children").GetInt32() != 0
        )
            throw new JsonException();
        var key = Text(rate, "rateKey", 4096);
        var source = CurrentSource.Identity;
        var money = Money(rate.GetProperty("net"), Text(hotel, "currency", 3));
        var roomCode = Text(room, "code", 160);
        var id = hotel.GetProperty("code").GetInt32().ToString(CultureInfo.InvariantCulture);
        var included = new List<HotelObligation>();
        List<HotelObligation>? local = null;
        if (
            rate.TryGetProperty("taxes", out var taxes)
            && taxes.ValueKind == JsonValueKind.Object
            && taxes.TryGetProperty("allIncluded", out var all)
            && all.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            local = [];
            if (
                !taxes.TryGetProperty("taxes", out var taxList)
                || taxList.ValueKind != JsonValueKind.Array
            )
                local = null;
            else
                foreach (var tax in taxList.EnumerateArray())
                {
                    if (
                        !tax.TryGetProperty("included", out var inc)
                        || inc.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    )
                    {
                        local = null;
                        break;
                    }
                    var obligation = new HotelObligation(
                        inc.GetBoolean() ? "Included" : "Excluded",
                        Money(tax.GetProperty("amount"), Text(tax, "currency", 3)),
                        "WholeStay"
                    );
                    if (inc.GetBoolean())
                        included.Add(obligation);
                    else
                        local?.Add(obligation);
                }
            if (!all.GetBoolean() && local is { Count: 0 })
                local = null;
            if (all.GetBoolean() && local is { Count: > 0 })
                local = null;
        }
        var windows = new List<HotelCancellationWindow>();
        if (
            rate.TryGetProperty("cancellationPolicies", out var policies)
            && policies.ValueKind == JsonValueKind.Array
        )
            foreach (var policy in policies.EnumerateArray())
            {
                var from = Text(policy, "from", 40);
                if (!(from.EndsWith('Z') || from.Length >= 6 && from[^6] is '+' or '-'))
                    throw new JsonException();
                windows.Add(
                    new(
                        DateTimeOffset.Parse(from, CultureInfo.InvariantCulture),
                        Money(policy.GetProperty("amount"), money.Currency),
                        "WholeStay",
                        Key
                    )
                );
            }
        var rateClass = rate.TryGetProperty("rateClass", out var classValue)
            ? classValue.GetString()
            : null;
        var cancelKind =
            windows.Count > 0 ? "SourcePolicies"
            : rateClass == "NRF" ? "NonRefundable"
            : "Unknown";
        var boardCode = Text(rate, "boardCode", 40);
        var conditions = new HotelConditionEvidence(
            cancelKind,
            [new(boardCode, null, null, Key)],
            windows.ToArray(),
            Key
        );
        var price = new HotelPriceEvidence(
            "SupplierCost",
            money,
            null,
            "WholeStay",
            Key,
            included.ToArray(),
            local?.ToArray()
        );
        var eligible =
            options.Value.PricingProfile == "Net"
            && Text(rate, "paymentType", 30) == "AT_WEB"
            && rateClass is "NOR" or "NRF"
            && !True(rate, "hotelMandatory")
            && !True(rate, "packaging")
            && !True(rate, "resident")
            && !rate.TryGetProperty("commission", out _)
            && !True(hotel, "paymentDataRequired");
        var reason =
            !eligible ? "ProfileUnsupported"
            : local is null ? "LocalAmountsUnknown"
            : cancelKind == "Unknown" ? "CancellationUnknown"
            : null;
        var semantic = Convert
            .ToHexString(
                SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            roomCode,
                            boardCode,
                            price,
                            conditions,
                        }
                    )
                )
            )
            .ToLowerInvariant();
        var terms = new StayTerms(
            new(
                id,
                Text(hotel, "name", 300),
                options.Value.Fictional && id == "900001" ? "1 Owned Fictional Square" : null,
                null,
                null
            ),
            Text(room, "name", 300),
            semantic,
            search.Dates,
            search.Occupancy,
            money,
            local?.Select(o => o.Amount!).ToArray(),
            [new(Text(rate, "boardName", 300), "CoverageUnknown")],
            new(
                cancelKind,
                windows
                    .Select(w => new ScopedFact(
                        "При отмене начиная с "
                            + w.From.ToString("O", CultureInfo.InvariantCulture)
                            + ": удержание "
                            + w.Amount.Amount.ToString(CultureInfo.InvariantCulture)
                            + " "
                            + w.Amount.Currency,
                        "WholeStay"
                    ))
                    .ToArray()
            ),
            rate.TryGetProperty("rateComments", out var comments)
            && comments.ValueKind == JsonValueKind.String
                ? [new(Text(rate, "rateComments", 4000), "SourceRate")]
                : [],
            eligible,
            "ImmediateTest"
        );
        var expiry = time.GetUtcNow().AddMinutes(5);
        return new(Key, "Test", Guid.NewGuid().ToString("N"), terms, null, reason is null, reason)
        {
            Source = source,
            Price = price,
            ConditionEvidence = conditions,
            RoomIdentity = roomCode,
            SelectionContext = new(
                2,
                JsonSerializer.Serialize(
                    new HotelbedsContext(source, key, CredentialIdentity),
                    Json
                )
            ),
            AdmissionBinding = new(
                2,
                source,
                CurrentSource.ConfigurationRevision,
                CurrentSource.PricingProfile,
                CurrentSource.ApprovalRevision,
                null,
                null,
                expiry,
                CurrentSource.TransportAccountBinding,
                TransportCredentialFingerprint: CredentialIdentity
            ),
        };
    }

    private HotelbedsContext Context(ProviderContextHandle handle)
    {
        var value =
            JsonSerializer.Deserialize<HotelbedsContext>(handle.Value, Json)
            ?? throw new JsonException();
        if (
            handle.Version != 2
            || value.Source != CurrentSource.Identity
            || value.CredentialIdentity != CredentialIdentity
            || Encoding.UTF8.GetByteCount(value.RateKey) > 4096
            || options.Value.Fictional != (http is HotelbedsFictionalHttpClient)
        )
            throw new JsonException();
        return value;
    }

    private async Task<JsonDocument> Send(string path, object? payload, CancellationToken ct)
    {
        var o = options.Value;
        if (o.Fictional != (http is HotelbedsFictionalHttpClient))
            throw new JsonException();
        using var request = new HttpRequestMessage(
            payload is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri("https://api-mtls.test.hotelbeds.com/hotel-api/1.0/" + path)
        );
        request.Headers.Add("Api-key", o.ApiKey);
        var signature = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                o.ApiKey
                    + o.Secret
                    + time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
            )
        );
        request.Headers.Add("X-Signature", Convert.ToHexString(signature).ToLowerInvariant());
        request.Headers.Accept.ParseAdd("application/json");
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: Json);
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Supplier failure.", null, response.StatusCode);
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024)
            throw new InvalidDataException("Response too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + read > 2 * 1024 * 1024)
                throw new InvalidDataException("Response too large.");
            bytes.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(
            bytes.ToArray(),
            new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false }
        );
    }

    private static string Date(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void ValidateDates(JsonElement value, StayDates dates)
    {
        if (
            Text(value, "checkIn", 10) != Date(dates.CheckIn)
            || Text(value, "checkOut", 10) != Date(dates.CheckOut)
        )
            throw new JsonException();
    }

    private static bool True(JsonElement value, string field)
    {
        if (!value.TryGetProperty(field, out var property))
            return false;
        return property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : throw new JsonException();
    }

    private static string Text(JsonElement value, string field, int max)
    {
        var text = value.GetProperty(field).GetString();
        return text is { Length: > 0 } && text.Length <= max && !text.Any(char.IsControl)
            ? text
            : throw new JsonException();
    }

    private static StayMoney Money(JsonElement amount, string currency)
    {
        var exact = decimal.Parse(
            amount.ValueKind == JsonValueKind.String ? amount.GetString()! : amount.GetRawText(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture
        );
        return new(
            decimal.Parse(
                exact.ToString("G29", CultureInfo.InvariantCulture),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture
            ),
            currency
        );
    }

    private static bool Safe(Exception ex) =>
        ex
            is JsonException
                or FormatException
                or ArgumentException
                or InvalidOperationException
                or KeyNotFoundException
                or HttpRequestException
                or OperationCanceledException
                or IOException
                or OverflowException;

    private static string Reason(Exception ex) =>
        ex switch
        {
            OperationCanceledException => "Timeout",
            InvalidDataException => "ResponseTooLarge",
            HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
                "SupplierRateLimit",
            HttpRequestException
            {
                StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            } => "SupplierAuthentication",
            HttpRequestException => "Transport",
            _ => "Decode",
        };

    private static HotelExecution<HotelOffer> Failure(Exception ex, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        return HotelExecution<HotelOffer>.Failed(Reason(ex));
    }

    private sealed record HotelbedsContext(
        HotelSourceIdentity Source,
        string RateKey,
        string CredentialIdentity
    );
}
