using Shouldly;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class OfferPipelineTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly IataCode Svo = IataCode.Create("SVO").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    private static Itinerary BuildItinerary(
        DateTimeOffset? departAt = null,
        DateTimeOffset? arriveAt = null,
        string carrier = "SU",
        string flightNumber = "SU1234"
    )
    {
        var depart = departAt ?? new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var arrive = arriveAt ?? depart.AddHours(2);
        var segment = Segment
            .Create(Led, Dme, depart, arrive, carrier, flightNumber, CabinClass.Economy)
            .Value;
        var slice = Slice.Create(new[] { segment }).Value;
        return Itinerary.Create(new[] { slice }).Value;
    }

    private static BookableOffer BuildBookable(
        decimal amount = 5000m,
        Itinerary? itinerary = null,
        string carrier = "SU",
        string flightNumber = "SU1234",
        DateTimeOffset? departAt = null
    )
    {
        var itin =
            itinerary
            ?? BuildItinerary(departAt: departAt, carrier: carrier, flightNumber: flightNumber);
        return new BookableOffer(
            OfferId.New(),
            itin,
            Money.Create(amount, Rub).Value,
            ProviderId.Duffel,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new FareConditions(false, false, "YECO", "Economy"),
            "ref-" + Guid.NewGuid()
        );
    }

    private static DeeplinkOffer BuildDeeplink(
        decimal amount = 4800m,
        Itinerary? itinerary = null,
        string carrier = "SU",
        string flightNumber = "SU1234",
        DateTimeOffset? departAt = null
    )
    {
        var itin =
            itinerary
            ?? BuildItinerary(departAt: departAt, carrier: carrier, flightNumber: flightNumber);
        return new DeeplinkOffer(
            OfferId.New(),
            itin,
            Money.Create(amount, Rub).Value,
            ProviderId.Travelpayouts,
            DateTimeOffset.UtcNow,
            new Uri("https://tp.example.com/deeplink"),
            "Aviasales"
        );
    }

    /// <summary>
    /// Builds a round-trip itinerary: outbound LED→DME with <paramref name="outboundCarrier"/>/<paramref name="outboundFlight"/>,
    /// inbound DME→LED with <paramref name="inboundCarrier"/>/<paramref name="inboundFlight"/>.
    /// </summary>
    private static Itinerary BuildRoundTripItinerary(
        string outboundCarrier = "SU",
        string outboundFlight = "SU1000",
        string inboundCarrier = "SU",
        string inboundFlight = "SU1001"
    )
    {
        var outDepart = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var outArrive = outDepart.AddHours(2);
        var inDepart = new DateTimeOffset(2026, 7, 8, 14, 0, 0, TimeSpan.Zero);
        var inArrive = inDepart.AddHours(2);

        var outSegment = Segment
            .Create(
                Led,
                Dme,
                outDepart,
                outArrive,
                outboundCarrier,
                outboundFlight,
                CabinClass.Economy
            )
            .Value;
        var inSegment = Segment
            .Create(Dme, Led, inDepart, inArrive, inboundCarrier, inboundFlight, CabinClass.Economy)
            .Value;

        var outSlice = Slice.Create(new[] { outSegment }).Value;
        var inSlice = Slice.Create(new[] { inSegment }).Value;
        return Itinerary.Create(new[] { outSlice, inSlice }).Value;
    }

    // ─── OfferDeduplicator ──────────────────────────────────────────────────────

    [Fact]
    public void Dedup_SameCarrierFlightDate_CheaperWins()
    {
        var expensive = BuildBookable(amount: 6000m);
        var cheap = BuildBookable(amount: 4000m);

        var result = OfferDeduplicator.Dedup(new[] { expensive, cheap });

        result.Count.ShouldBe(1);
        result[0].TotalAmount.Amount.ShouldBe(4000m);
    }

    [Fact]
    public void Dedup_SamePriceBookableAndDeeplink_BookableWins()
    {
        // Same carrier/flight/date, same price — Bookable should beat Deeplink
        var depart = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var bookable = BuildBookable(amount: 5000m, departAt: depart);
        var deeplink = BuildDeeplink(amount: 5000m, departAt: depart);

        var result = OfferDeduplicator.Dedup(new Offer[] { deeplink, bookable });

        result.Count.ShouldBe(1);
        result[0].ShouldBeOfType<BookableOffer>();
    }

    [Fact]
    public void Dedup_DifferentFlights_BothKept()
    {
        var offer1 = BuildBookable(carrier: "SU", flightNumber: "SU1234");
        var offer2 = BuildBookable(carrier: "S7", flightNumber: "S71001");

        var result = OfferDeduplicator.Dedup(new[] { offer1, offer2 });

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void Roundtrips_with_different_inbound_are_not_deduped()
    {
        // Same outbound SU1000, but different inbound: SU1001 vs S7 2000
        var rtA = BuildBookable(itinerary: BuildRoundTripItinerary("SU", "SU1000", "SU", "SU1001"));
        var rtB = BuildBookable(itinerary: BuildRoundTripItinerary("SU", "SU1000", "S7", "S72000"));

        var result = OfferDeduplicator.Dedup(new Offer[] { rtA, rtB });

        result.Count.ShouldBe(
            2,
            "round-trips with the same outbound but different inbound must not be deduped"
        );
    }

    [Fact]
    public void Identical_roundtrips_are_deduped()
    {
        // Identical outbound AND inbound — should collapse to 1
        var cheap = BuildBookable(
            amount: 4000m,
            itinerary: BuildRoundTripItinerary("SU", "SU1000", "SU", "SU1001")
        );
        var expensive = BuildBookable(
            amount: 6000m,
            itinerary: BuildRoundTripItinerary("SU", "SU1000", "SU", "SU1001")
        );

        var result = OfferDeduplicator.Dedup(new Offer[] { cheap, expensive });

        result.Count.ShouldBe(1, "identical round-trips must be deduped to a single offer");
        result[0].TotalAmount.Amount.ShouldBe(4000m);
    }

    // ─── OfferRanker ────────────────────────────────────────────────────────────

    [Fact]
    public void Rank_SortedByPriceAscending()
    {
        var o1 = BuildBookable(amount: 9000m, carrier: "SU", flightNumber: "SU0001");
        var o2 = BuildBookable(amount: 3000m, carrier: "S7", flightNumber: "S70002");
        var o3 = BuildBookable(amount: 6000m, carrier: "FV", flightNumber: "FV0003");

        var result = OfferRanker.Rank(new[] { o1, o2, o3 });

        result[0].TotalAmount.Amount.ShouldBe(3000m);
        result[1].TotalAmount.Amount.ShouldBe(6000m);
        result[2].TotalAmount.Amount.ShouldBe(9000m);
    }

    [Fact]
    public void Rank_EqualPrice_ShorterDurationFirst()
    {
        // Same price, different durations — need different carrier/flight so Dedup doesn't interfere
        var depart = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        var longFlight = BuildItinerary(
            departAt: depart,
            arriveAt: depart.AddHours(4),
            carrier: "SU",
            flightNumber: "SU1111"
        );
        var shortFlight = BuildItinerary(
            departAt: depart,
            arriveAt: depart.AddHours(2),
            carrier: "S7",
            flightNumber: "S72222"
        );

        var o1 = BuildBookable(amount: 5000m, itinerary: longFlight);
        var o2 = BuildBookable(amount: 5000m, itinerary: shortFlight);

        var result = OfferRanker.Rank(new[] { o1, o2 });

        result[0].Itinerary.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(2));
        result[1].Itinerary.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(4));
    }

    [Fact]
    public void Rank_TopCapApplied()
    {
        var offers = Enumerable
            .Range(1, 10)
            .Select(i => BuildBookable(amount: i * 100m, carrier: "SU", flightNumber: $"SU{i:D4}"))
            .ToList();

        var result = OfferRanker.Rank(offers, top: 5);

        result.Count.ShouldBe(5);
    }
}
