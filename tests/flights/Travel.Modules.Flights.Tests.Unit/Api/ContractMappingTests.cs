using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Api;

public sealed class ContractMappingTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    private static Itinerary BuildItinerary(int sliceCount = 1)
    {
        var outboundSeg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var outbound = Slice.Create([outboundSeg]).Value;

        if (sliceCount == 1)
            return Itinerary.Create([outbound]).Value;

        // Round-trip: inbound must mirror outbound endpoints (DME→LED)
        var inboundSeg = Segment
            .Create(
                Dme,
                Led,
                new DateTimeOffset(2026, 7, 29, 14, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 29, 17, 0, 0, TimeSpan.Zero),
                "SU",
                "101",
                CabinClass.Economy
            )
            .Value;
        var inbound = Slice.Create([inboundSeg]).Value;
        return Itinerary.Create([outbound, inbound]).Value;
    }

    private static BookableOffer BuildBookableOffer() =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(5420m, Rub).Value,
            Provider: ProviderId.Duffel,
            FetchedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(20),
            FareConditions: new FareConditions(false, false, null, null),
            ProviderOfferRef: "off_test_abc123"
        );

    private static DeeplinkOffer BuildDeeplinkOffer() =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(4800m, Rub).Value,
            Provider: ProviderId.Travelpayouts,
            FetchedAt: DateTimeOffset.UtcNow,
            DeeplinkUrl: new Uri("https://tp.travel/xyz"),
            PartnerName: "Travelpayouts"
        );

    // ── OfferDto.From ────────────────────────────────────────────────────────────

    [Fact]
    public void OfferDto_From_BookableOffer_PopulatesProviderOfferRefAndExpiresAt()
    {
        var offer = BuildBookableOffer();
        var dto = OfferDto.From(offer);

        dto.ProviderOfferRef.ShouldBe("off_test_abc123");
        dto.ExpiresAt.ShouldNotBeNull();
        dto.DeeplinkUrl.ShouldBeNull();
        dto.PartnerName.ShouldBeNull();
        dto.TotalAmount.ShouldBe(5420m);
        dto.Currency.ShouldBe("RUB");
        dto.Provider.ShouldBe("duffel");
    }

    [Fact]
    public void OfferDto_From_DeeplinkOffer_PopulatesDeeplinkUrlAndPartnerName()
    {
        var offer = BuildDeeplinkOffer();
        var dto = OfferDto.From(offer);

        dto.DeeplinkUrl.ShouldBe("https://tp.travel/xyz");
        dto.PartnerName.ShouldBe("Travelpayouts");
        dto.ProviderOfferRef.ShouldBeNull();
        dto.ExpiresAt.ShouldBeNull();
        dto.TotalAmount.ShouldBe(4800m);
        dto.Provider.ShouldBe("travelpayouts");
    }

    // ── ItineraryDto.From ────────────────────────────────────────────────────────

    [Fact]
    public void ItineraryDto_From_OneWay_HasOneSlice_IsRoundTripFalse()
    {
        var itinerary = BuildItinerary(sliceCount: 1);
        var dto = ItineraryDto.From(itinerary);

        dto.Slices.Length.ShouldBe(1);
        dto.IsRoundTrip.ShouldBeFalse();
        dto.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ItineraryDto_From_RoundTrip_HasTwoSlices_IsRoundTripTrue()
    {
        var itinerary = BuildItinerary(sliceCount: 2);
        var dto = ItineraryDto.From(itinerary);

        dto.Slices.Length.ShouldBe(2);
        dto.IsRoundTrip.ShouldBeTrue();
    }

    [Fact]
    public void SliceDto_From_RoundTripsSegmentCount()
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create([seg]).Value;
        var dto = SliceDto.From(slice);

        dto.Segments.Length.ShouldBe(1);
        dto.Origin.ShouldBe("LED");
        dto.Destination.ShouldBe("DME");
        dto.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    // ── QuotedOfferResponse price-change fields ──────────────────────────────────

    [Fact]
    public void QuotedOfferResponse_carries_price_change_fields_on_requote()
    {
        // Arrange: a QuotedOfferResult that represents a successful re-quote where
        // the provider returned a higher price than the cached offer.
        var aggregateId = Guid.NewGuid();
        var offer = BuildBookableOffer();
        var oldMoney = Money.Create(5420m, Rub).Value;
        var newMoney = Money.Create(5999m, Rub).Value;

        var result = new QuotedOfferResult(
            AggregateId: aggregateId,
            Offer: offer,
            PriceChanged: true,
            OldAmount: oldMoney,
            NewAmount: newMoney
        );

        // Act: map QuotedOfferResult → QuotedOfferResponse (the same mapping used by QuoteOfferEndpoint)
        var response = new QuotedOfferResponse(
            AggregateId: result.AggregateId,
            Offer: OfferDto.From(result.Offer),
            PriceChanged: result.PriceChanged,
            OldAmount: result.OldAmount?.Amount,
            OldCurrency: result.OldAmount?.Currency.Value,
            NewAmount: result.NewAmount?.Amount,
            NewCurrency: result.NewAmount?.Currency.Value
        );

        // Assert: all price-change fields are surfaced correctly on the HTTP DTO.
        response.AggregateId.ShouldBe(aggregateId);
        response.PriceChanged.ShouldBeTrue();
        response.OldAmount.ShouldBe(5420m);
        response.OldCurrency.ShouldBe("RUB");
        response.NewAmount.ShouldBe(5999m);
        response.NewCurrency.ShouldBe("RUB");
    }

    [Fact]
    public void QuotedOfferResponse_price_change_fields_are_null_when_price_unchanged()
    {
        var result = new QuotedOfferResult(
            AggregateId: Guid.NewGuid(),
            Offer: BuildBookableOffer(),
            PriceChanged: false,
            OldAmount: null,
            NewAmount: null
        );

        var response = new QuotedOfferResponse(
            AggregateId: result.AggregateId,
            Offer: OfferDto.From(result.Offer),
            PriceChanged: result.PriceChanged,
            OldAmount: result.OldAmount?.Amount,
            OldCurrency: result.OldAmount?.Currency.Value,
            NewAmount: result.NewAmount?.Amount,
            NewCurrency: result.NewAmount?.Currency.Value
        );

        response.PriceChanged.ShouldBeFalse();
        response.OldAmount.ShouldBeNull();
        response.OldCurrency.ShouldBeNull();
        response.NewAmount.ShouldBeNull();
        response.NewCurrency.ShouldBeNull();
    }

    // ── OrderResponseMapper malformed JSON ───────────────────────────────────────

    [Fact]
    public void Mapper_handles_empty_object_itinerary_json_gracefully()
    {
        // ItineraryJson = "{}" deserializes to a non-null Itinerary with null Slices
        // and null TotalDuration (because the [JsonConstructor] was not invoked with
        // matching property names). The mapper must not throw ArgumentNullException.
        var aggregateId = Guid.NewGuid();
        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: Guid.NewGuid(),
            ProviderOrderId: "ord_empty",
            Status: "Confirmed",
            TotalAmount: 5000m,
            Currency: "RUB",
            ItineraryJson: "{}",
            PassengerInfoJson: "{}",
            TicketNumbers: [],
            BookedAt: DateTimeOffset.UtcNow,
            TicketedAt: null,
            CancelledAt: null,
            RefundedAt: null
        );

        // Act — must not throw
        var response = OrderResponseMapper.From(view, NullLogger.Instance);

        // Assert — falls back to an empty itinerary
        response.ShouldNotBeNull();
        response.Itinerary.ShouldNotBeNull();
        response.Itinerary.Slices.ShouldBeEmpty();
        response.Itinerary.TotalDuration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Mapper_logs_warning_on_malformed_itinerary_json()
    {
        // Arrange
        var aggregateId = Guid.NewGuid();
        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: Guid.NewGuid(),
            ProviderOrderId: "ord_test",
            Status: "Confirmed",
            TotalAmount: 5000m,
            Currency: "RUB",
            ItineraryJson: "NOT VALID JSON {{{{",
            PassengerInfoJson: "{}",
            TicketNumbers: [],
            BookedAt: DateTimeOffset.UtcNow,
            TicketedAt: null,
            CancelledAt: null,
            RefundedAt: null
        );
        var logger = new CapturingLogger();

        // Act
        var response = OrderResponseMapper.From(view, logger);

        // Assert — falls back to empty itinerary and logs a warning
        response.ShouldNotBeNull();
        response.Itinerary.ShouldNotBeNull();
        response.Itinerary.Slices.Length.ShouldBe(0);

        logger.HasWarning.ShouldBeTrue("Expected a warning log entry for malformed ItineraryJson.");
    }

    /// <summary>
    /// Minimal ILogger that records whether any Warning-or-above message was emitted.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public bool HasWarning { get; private set; }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
                HasWarning = true;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    [Fact]
    public void SegmentDto_From_MapsAllFields()
    {
        var depart = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        var arrive = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var seg = Segment
            .Create(Led, Dme, depart, arrive, "SU", "SU100", CabinClass.Business)
            .Value;
        var dto = SegmentDto.From(seg);

        dto.Origin.ShouldBe("LED");
        dto.Destination.ShouldBe("DME");
        dto.DepartAt.ShouldBe(depart);
        dto.ArriveAt.ShouldBe(arrive);
        dto.CarrierCode.ShouldBe("SU");
        dto.FlightNumber.ShouldBe("SU100");
        dto.CabinClass.ShouldBe("business");
    }

    // ── SearchRequest → SearchCriteria validation ─────────────────────────────────

    /// <summary>
    /// Mirrors the mapping logic in <see cref="SearchEndpoint.Post"/>:
    /// valid body + valid query-param currency → SearchCriteria succeeds.
    /// </summary>
    [Fact]
    public void SearchRequest_valid_body_and_currency_maps_to_SearchCriteria()
    {
        var req = new SearchRequest(
            Origin: "LED",
            Destination: "DME",
            DepartureDate: new DateOnly(2026, 9, 1),
            ReturnDate: null,
            PassengerCount: 1,
            CabinClass: "economy"
        );

        var currencyCode = CurrencyCode.Create("RUB");
        currencyCode.IsError.ShouldBeFalse();

        var origin = IataCode.Create(req.Origin);
        var dest = IataCode.Create(req.Destination);
        var cabin = CabinClass.Parse(req.CabinClass);

        var sc = SearchCriteria.Create(
            origin.Value,
            dest.Value,
            req.DepartureDate,
            req.ReturnDate,
            req.PassengerCount,
            cabin.Value,
            currencyCode.Value,
            locale: "en"
        );

        sc.IsError.ShouldBeFalse();
        sc.Value.Origin.Value.ShouldBe("LED");
        sc.Value.Destination.Value.ShouldBe("DME");
        sc.Value.Currency.Value.ShouldBe("RUB");
        sc.Value.Locale.ShouldBe("en");
        sc.Value.IsRoundTrip.ShouldBeFalse();
    }

    /// <summary>
    /// Documents the locale-boundary contract: <see cref="SearchCriteria.Create"/> accepts any
    /// non-empty locale string without validation. Locale normalisation (restricting to a
    /// supported set such as "ru" / "en") is the responsibility of the endpoint layer
    /// (<see cref="Travel.Modules.Flights.Api.Endpoints.SearchEndpoint"/> /
    /// <see cref="Travel.Modules.Flights.Api.Endpoints.NlSearchEndpoint"/>) via
    /// <c>HttpRequestExtensions.ResolveLocale</c>, NOT the domain. This lets the domain remain
    /// agnostic of locale policy and makes the boundary explicit in tests.
    /// </summary>
    [Fact]
    public void SearchCriteria_accepts_arbitrary_locale_string_endpoint_does_normalisation()
    {
        var origin = IataCode.Create("LED").Value;
        var dest = IataCode.Create("DME").Value;
        var currency = CurrencyCode.Create("RUB").Value;

        // "zh" would be rejected by the endpoint's ResolveLocale (not in SupportedLocales)
        // but the domain value object imposes no such restriction.
        var sc = SearchCriteria.Create(
            origin,
            dest,
            new DateOnly(2026, 9, 1),
            returnDate: null,
            passengerCount: 1,
            cabinClass: CabinClass.Economy,
            currency: currency,
            locale: "zh"
        );

        sc.IsError.ShouldBeFalse();
        sc.Value.Locale.ShouldBe("zh");
    }

    [Fact]
    public void SearchRequest_same_origin_destination_produces_validation_error()
    {
        var origin = IataCode.Create("LED").Value;
        var cabin = CabinClass.Economy;
        var currency = CurrencyCode.Create("RUB").Value;

        var sc = SearchCriteria.Create(
            origin,
            origin, // same as origin
            new DateOnly(2026, 9, 1),
            returnDate: null,
            passengerCount: 1,
            cabinClass: cabin,
            currency: currency
        );

        sc.IsError.ShouldBeTrue();
        sc.FirstError.Code.ShouldBe("SearchCriteria.SameOriginDestination");
    }

    [Fact]
    public void SearchRequest_return_before_departure_produces_validation_error()
    {
        var origin = IataCode.Create("LED").Value;
        var dest = IataCode.Create("SVO").Value;
        var currency = CurrencyCode.Create("RUB").Value;

        var sc = SearchCriteria.Create(
            origin,
            dest,
            departureDate: new DateOnly(2026, 9, 10),
            returnDate: new DateOnly(2026, 9, 5), // before departure
            passengerCount: 1,
            cabinClass: CabinClass.Economy,
            currency: currency
        );

        sc.IsError.ShouldBeTrue();
        sc.FirstError.Code.ShouldBe("SearchCriteria.ReturnBeforeDeparture");
    }

    // ── PassengerInfoDto → PassengerInfo validation ───────────────────────────────

    private static PassengerInfo BuildValidPassenger(DateOnly? today = null)
    {
        var gender = Gender.Parse("male").Value;
        var phone = PhoneNumber.Create("+79161234567").Value;
        var reference = today ?? new DateOnly(2026, 7, 15);

        return PassengerInfo
            .Create(
                "Ivan",
                "Petrov",
                new DateOnly(1990, 1, 1),
                gender,
                "ivan@test.com",
                phone,
                reference
            )
            .Value;
    }

    [Fact]
    public void PassengerInfoDto_valid_maps_to_PassengerInfo()
    {
        var dto = new PassengerInfoDto(
            "Ivan",
            "Petrov",
            new DateOnly(1990, 1, 1),
            "male",
            "ivan@test.com",
            "+79161234567"
        );

        var gender = Gender.Parse(dto.Gender);
        gender.IsError.ShouldBeFalse();

        var phone = PhoneNumber.Create(dto.Phone);
        phone.IsError.ShouldBeFalse();

        var today = new DateOnly(2026, 7, 15);
        var passenger = PassengerInfo.Create(
            dto.GivenName,
            dto.FamilyName,
            dto.DateOfBirth,
            gender.Value,
            dto.Email,
            phone.Value,
            today
        );

        passenger.IsError.ShouldBeFalse();
        passenger.Value.GivenName.ShouldBe("Ivan");
        passenger.Value.FamilyName.ShouldBe("Petrov");
        passenger.Value.Email.ShouldBe("ivan@test.com");
    }

    [Fact]
    public void PassengerInfoDto_invalid_email_produces_validation_error()
    {
        var gender = Gender.Parse("female").Value;
        var phone = PhoneNumber.Create("+79161234567").Value;
        var today = new DateOnly(2026, 7, 15);

        var passenger = PassengerInfo.Create(
            "Anna",
            "Sidorova",
            new DateOnly(1992, 5, 15),
            gender,
            "not-an-email",
            phone,
            today
        );

        passenger.IsError.ShouldBeTrue();
        passenger.FirstError.Code.ShouldBe("PassengerInfo.EmailInvalid");
    }

    [Fact]
    public void PassengerInfoDto_future_date_of_birth_produces_validation_error()
    {
        var gender = Gender.Parse("male").Value;
        var phone = PhoneNumber.Create("+79161234567").Value;
        var today = new DateOnly(2026, 7, 15);

        var passenger = PassengerInfo.Create(
            "Ivan",
            "Petrov",
            dateOfBirth: today.AddDays(1), // future
            gender,
            "ivan@test.com",
            phone,
            today
        );

        passenger.IsError.ShouldBeTrue();
        passenger.FirstError.Code.ShouldBe("PassengerInfo.DateOfBirthFuture");
    }
}
