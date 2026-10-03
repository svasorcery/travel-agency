using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class MultiLegReplayTests
{
    // Frozen pre-M2.5 event shapes. The mirrored return starts before the outbound
    // arrives: old construction allowed this, so replay must remain unconditional.
    internal const string HistoricalQuoteJson = """
        {
          "OfferId": { "Value": "11111111-1111-1111-1111-111111111111", "IsEmpty": false },
          "Itinerary": {
            "Slices": [
              {
                "Origin": { "Value": "LED" }, "Destination": { "Value": "JFK" },
                "Segments": [{
                  "Origin": { "Value": "LED" }, "Destination": { "Value": "JFK" },
                  "DepartAt": "2030-06-01T10:00:00+03:00", "ArriveAt": "2030-06-01T19:00:00+03:00",
                  "CarrierCode": "SU", "FlightNumber": "100", "Cabin": { "Code": "economy" }, "Duration": "09:00:00"
                }],
                "Duration": { "Value": "09:00:00" },
                "DepartAt": "2030-06-01T10:00:00+03:00", "ArriveAt": "2030-06-01T19:00:00+03:00"
              },
              {
                "Origin": { "Value": "JFK" }, "Destination": { "Value": "LED" },
                "Segments": [{
                  "Origin": { "Value": "JFK" }, "Destination": { "Value": "LED" },
                  "DepartAt": "2030-06-01T08:00:00-04:00", "ArriveAt": "2030-06-02T00:00:00+03:00",
                  "CarrierCode": "SU", "FlightNumber": "101", "Cabin": { "Code": "economy" }, "Duration": "09:00:00"
                }],
                "Duration": { "Value": "09:00:00" },
                "DepartAt": "2030-06-01T08:00:00-04:00", "ArriveAt": "2030-06-02T00:00:00+03:00"
              }
            ],
            "TotalDuration": { "Value": "18:00:00" }, "IsOneWay": false, "IsRoundTrip": true
          },
          "TotalAmount": { "Amount": 100, "Currency": { "Value": "USD" } },
          "ExpiresAt": "2030-06-01T13:00:00+03:00", "ProviderRef": "off_historical",
          "QuotedAt": "2030-06-01T12:00:00+03:00", "FareConditions": null,
          "OccurredAt": "2030-06-01T12:00:00+03:00"
        }
        """;

    internal const string HistoricalHeldV1Json = """
        { "OrderId": "ord_v1", "Passenger": {
          "GivenName": "Demo", "FamilyName": "Traveler", "DateOfBirth": "1990-01-01",
          "Gender": { "Code": "unspecified" }, "Email": "demo@example.invalid", "Phone": { "Value": "+79990000000" }
        }, "HeldUntil": "2030-06-01T15:00:00+03:00", "HeldAt": "2030-06-01T12:30:00+03:00",
          "OwnerUserId": "22222222-2222-2222-2222-222222222222", "OccurredAt": "2030-06-01T12:30:00+03:00" }
        """;
    internal const string HistoricalHeldV2Json = """
        { "OrderId": "ord_v2", "PassengerSnapshot": { "FormatVersion": 1, "Ciphertext": "frozen-singular-ciphertext" },
          "HeldUntil": "2030-06-01T15:00:00+03:00", "HeldAt": "2030-06-01T12:30:00+03:00",
          "OwnerUserId": "22222222-2222-2222-2222-222222222222", "OccurredAt": "2030-06-01T12:30:00+03:00" }
        """;
    internal const string HistoricalHeldV3Json = """
        { "OrderId": "ord_v3", "PassengerSnapshot": { "FormatVersion": 1, "Ciphertext": "frozen-party-ciphertext" },
          "HeldUntil": "2030-06-01T15:00:00+03:00", "HeldAt": "2030-06-01T12:30:00+03:00",
          "OwnerUserId": "22222222-2222-2222-2222-222222222222", "QuoteRevision": "33333333-3333-3333-3333-333333333333",
          "PassengerCount": 2, "OccurredAt": "2030-06-01T12:30:00+03:00" }
        """;

    [Fact]
    public void Kindless_historical_quote_replays_without_fresh_graph_validation_or_timestamp_shift()
    {
        HistoricalQuoteJson.ShouldNotContain("JourneyKind");
        var quote = JsonSerializer.Deserialize<OfferQuoted>(HistoricalQuoteJson)!;
        var aggregate = new BookingAggregate();
        aggregate.Apply(quote);
        aggregate.Itinerary.ShouldBeSameAs(quote.Itinerary);
        aggregate.Itinerary!.JourneyKind.ShouldBe(JourneyKind.RoundTrip);
        Itinerary.Create(aggregate.Itinerary.Slices).IsError.ShouldBeTrue();
        aggregate.Itinerary.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(18));
        aggregate
            .Itinerary.Slices[0]
            .DepartAt.ShouldBe(DateTimeOffset.Parse("2030-06-01T10:00:00+03:00"));
        aggregate.Itinerary.Slices[0].DepartAt.Offset.ShouldBe(TimeSpan.FromHours(3));
        var serialized = JsonSerializer.Serialize(quote);
        serialized.ShouldContain("2030-06-01T10:00:00+03:00");
        serialized.ShouldContain("2030-06-01T08:00:00-04:00");
        serialized.ShouldNotContain("JourneyKind");
        aggregate.QuoteBinding.ShouldBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Historical_held_versions_preserve_payload_and_confirm_cancel_without_keys(
        int version
    )
    {
        var aggregate = new BookingAggregate();
        aggregate.Apply(JsonSerializer.Deserialize<OfferQuoted>(HistoricalQuoteJson)!);
        ApplyHistoricalHeld(aggregate, version);
        aggregate.Status.ShouldBe(BookingStatus.Held);
        aggregate.PassengerCount.ShouldBe(version == 3 ? 2 : 1);
        aggregate.BookedAt!.Value.Offset.ShouldBe(TimeSpan.FromHours(3));
        aggregate
            .DecideConfirm(DateTimeOffset.Parse("2030-06-01T12:45:00+03:00"))
            .ShouldBeOfType<BookingTransitionDecision.Allowed>();
        aggregate.DecideCancel().ShouldBeOfType<BookingTransitionDecision.Allowed>();
        if (version == 1)
            aggregate.Passenger!.GivenName.ShouldBe("Demo");
        if (version == 2)
            aggregate.ProtectedPassenger!.Ciphertext.ShouldBe("frozen-singular-ciphertext");
        if (version == 3)
            aggregate.ProtectedPassengerParty!.Ciphertext.ShouldBe("frozen-party-ciphertext");
        aggregate.Apply(
            new OrderConfirmed(
                "ord_confirmed",
                PaymentRef.New(),
                DateTimeOffset.Parse("2030-06-01T12:45:00+03:00")
            )
        );
        aggregate.DecideCancel().ShouldBeOfType<BookingTransitionDecision.Allowed>();
        aggregate.Apply(
            new OrderCancelled(CancelReason.User, DateTimeOffset.Parse("2030-06-01T12:50:00+03:00"))
        );
        aggregate.Status.ShouldBe(BookingStatus.Cancelled);
        aggregate.Itinerary!.Slices.Count.ShouldBe(2);
    }

    [Fact]
    public void Four_leg_quote_requote_and_held_replay_preserves_all_ordered_slices()
    {
        var quote = FourLegQuote();
        var aggregate = new BookingAggregate();
        aggregate.Apply(JsonSerializer.Deserialize<OfferQuoted>(JsonSerializer.Serialize(quote))!);
        aggregate.Itinerary!.JourneyKind.ShouldBe(JourneyKind.MultiLeg);
        var refreshed = FourLegReQuote(quote);
        aggregate.Apply(
            JsonSerializer.Deserialize<OfferReQuoted>(JsonSerializer.Serialize(refreshed))!
        );
        aggregate.Itinerary.Slices.Count.ShouldBe(4);
        aggregate.Itinerary.Slices[3].Segments[0].FlightNumber.ShouldBe("changed-leg-four");
        ApplyHistoricalHeld(aggregate, 3);
        aggregate.Itinerary.Slices.Count.ShouldBe(4);
        aggregate.Itinerary.TotalDuration.Value.ShouldBe(TimeSpan.FromHours(60));
        aggregate.ProtectedPassengerParty!.Ciphertext.ShouldBe("frozen-party-ciphertext");
    }

    internal static void ApplyHistoricalHeld(BookingAggregate aggregate, int version)
    {
        switch (version)
        {
            case 1:
                aggregate.Apply(JsonSerializer.Deserialize<OfferHeld>(HistoricalHeldV1Json)!);
                break;
            case 2:
                aggregate.Apply(JsonSerializer.Deserialize<OfferHeldV2>(HistoricalHeldV2Json)!);
                break;
            case 3:
                aggregate.Apply(JsonSerializer.Deserialize<OfferHeldV3>(HistoricalHeldV3Json)!);
                break;
        }
    }

    internal static OfferQuoted FourLegQuote()
    {
        var date = new DateTimeOffset(2030, 6, 1, 10, 0, 0, TimeSpan.FromHours(3));
        var airports = new[] { "LED", "DME", "VKO", "JFK", "LAX" };
        var slices = Enumerable
            .Range(0, 4)
            .Select(i =>
                Slice
                    .Create([
                        Segment
                            .Create(
                                IataCode.Create(airports[i]).Value,
                                IataCode.Create(airports[i + 1]).Value,
                                date.AddDays(i * 2),
                                date.AddDays(i * 2).AddHours(15),
                                "SU",
                                $"{100 + i}",
                                CabinClass.Economy
                            )
                            .Value,
                    ])
                    .Value
            )
            .ToArray();
        return new OfferQuoted(
            OfferId.New(),
            Itinerary.Create(slices).Value,
            Money.Create(100, CurrencyCode.Create("USD").Value).Value,
            date.AddHours(2),
            "off_four",
            date
        );
    }

    internal static OfferReQuoted FourLegReQuote(OfferQuoted quote)
    {
        var slices = quote.Itinerary.Slices.ToArray();
        var last = slices[3].Segments[0];
        slices[3] = Slice
            .Create([
                Segment
                    .Create(
                        last.Origin,
                        last.Destination,
                        last.DepartAt,
                        last.ArriveAt,
                        last.CarrierCode,
                        "changed-leg-four",
                        last.Cabin
                    )
                    .Value,
            ])
            .Value;
        var refreshed = new BookableOffer(
            OfferId.New(),
            Itinerary.Create(slices).Value,
            quote.TotalAmount,
            ProviderId.Duffel,
            quote.QuotedAt,
            quote.ExpiresAt,
            new FareConditions(false, false, null, null),
            quote.ProviderRef
        );
        return new OfferReQuoted(
            refreshed.Id,
            quote.TotalAmount,
            quote.TotalAmount,
            quote.QuotedAt,
            refreshed
        );
    }
}
