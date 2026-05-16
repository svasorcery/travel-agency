using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class OfferTests
{
    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Jfk = IataCode.Create("JFK").Value;
    private static readonly DateTimeOffset BaseOut = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

    private static Itinerary MakeItinerary()
    {
        var segment = Segment
            .Create(Led, Jfk, BaseOut, BaseOut.AddHours(9), "SU", "100", CabinClass.Economy)
            .Value;
        var slice = Slice.Create([segment]).Value;
        return Itinerary.Create([slice]).Value;
    }

    private static Money MakeMoney() => Money.Create(5420m, CurrencyCode.Create("RUB").Value).Value;

    private static BookableOffer MakeBookableOffer() =>
        new(
            OfferId.New(),
            MakeItinerary(),
            MakeMoney(),
            ProviderId.Duffel,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new FareConditions(true, false, "YOWUS", null),
            "duffel-offer-ref-001"
        );

    private static DeeplinkOffer MakeDeeplinkOffer() =>
        new(
            OfferId.New(),
            MakeItinerary(),
            MakeMoney(),
            ProviderId.Travelpayouts,
            DateTimeOffset.UtcNow,
            new Uri("https://tp.media/r?marker=123"),
            "Travelpayouts"
        );

    [Fact]
    public void BookableOffer_is_assignable_to_Offer()
    {
        Offer offer = MakeBookableOffer();
        offer.ShouldBeOfType<BookableOffer>();
        (offer is Offer).ShouldBeTrue();
    }

    [Fact]
    public void DeeplinkOffer_is_assignable_to_Offer()
    {
        Offer offer = MakeDeeplinkOffer();
        offer.ShouldBeOfType<DeeplinkOffer>();
        (offer is Offer).ShouldBeTrue();
    }

    [Fact]
    public void Pattern_match_distinguishes_BookableOffer_from_DeeplinkOffer()
    {
        Offer bookable = MakeBookableOffer();
        Offer deeplink = MakeDeeplinkOffer();

        var bookableResult = bookable switch
        {
            BookableOffer b => b.ProviderOfferRef,
            DeeplinkOffer d => "deeplink",
            _ => "?",
        };

        var deeplinkResult = deeplink switch
        {
            BookableOffer b => b.ProviderOfferRef,
            DeeplinkOffer d => "deeplink",
            _ => "?",
        };

        bookableResult.ShouldBe("duffel-offer-ref-001");
        deeplinkResult.ShouldBe("deeplink");
    }
}
