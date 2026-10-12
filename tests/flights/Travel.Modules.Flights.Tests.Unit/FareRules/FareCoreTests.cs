using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class FareCoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Known_zero_is_not_unknown()
    {
        var zero = Money.Create(0m, CurrencyCode.Create("GBP").Value).Value;
        var result = FareCondition.Create(FarePermission.Allowed, zero, "Known", "Known");
        result.IsError.ShouldBeFalse();
        result.Value.Penalty!.Amount.ShouldBe(0m);
        FareCondition.NotProvided.Permission.ShouldBe(FarePermission.Unknown);
        FareCondition.NotProvided.Penalty.ShouldBeNull();
        JsonSerializer.Serialize(FarePermission.NotAllowed).ShouldBe("\"NotAllowed\"");
        FareCondition
            .Create(FarePermission.Allowed, null, "Known", "NotProvided")
            .IsError.ShouldBeFalse();
        FareCondition
            .Create((FarePermission)99, null, "Known", "NotProvided")
            .IsError.ShouldBeTrue();
        FareCondition.Create(FarePermission.Allowed, null, "Known", "Known").IsError.ShouldBeTrue();
        var invalid = JsonSerializer.Deserialize<Money>(
            "{\"Amount\":1,\"Currency\":{\"Value\":\"BAD!\"}}"
        )!;
        FareCondition
            .Create(FarePermission.Allowed, invalid, "Known", "Known")
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Graph_preserves_unknown_and_rejects_missing_duplicate_and_invalid_pairs()
    {
        var facts = new[]
        {
            new FareSegmentFact(0, 0, 0, "Y", "Economy", 0, null),
            new FareSegmentFact(0, 0, 1, null, null, null, 1),
        };
        var evidence = Evidence(facts).Value;
        evidence.Passengers[0].CheckedBaggageQuantity.ShouldBe(0);
        evidence.Passengers[0].CarryOnBaggageQuantity.ShouldBeNull();
        Evidence(facts.Take(1)).IsError.ShouldBeTrue();
        Evidence(new[] { facts[0], facts[0] }).IsError.ShouldBeTrue();
        Evidence(new[] { facts[0] with { CarryOnBaggageQuantity = -1 }, facts[1] })
            .IsError.ShouldBeTrue();
        Evidence(new[] { facts[0] with { CarryOnBaggageQuantity = 100 }, facts[1] })
            .IsError.ShouldBeFalse();
        Evidence(new[] { facts[0] with { FareBasisCode = new string('X', 65) }, facts[1] })
            .IsError.ShouldBeTrue();
        Evidence(new[] { facts[0] with { FareBasisCode = "X\u202e" }, facts[1] })
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Largest_graph_is_complete_bounded_and_defensively_copied()
    {
        var counts = new[] { 16, 16, 16, 16 };
        var facts = Enumerable
            .Range(0, 4)
            .SelectMany(leg =>
                Enumerable
                    .Range(0, 16)
                    .SelectMany(segment =>
                        Enumerable
                            .Range(0, 9)
                            .Select(adult => new FareSegmentFact(
                                leg,
                                segment,
                                adult,
                                null,
                                null,
                                null,
                                null
                            ))
                    )
            )
            .ToArray();
        var result = FareRuleEvidence.Create(
            Now,
            Now.AddHours(1),
            Now.AddDays(1),
            9,
            counts,
            FareCondition.NotProvided,
            FareCondition.NotProvided,
            Enumerable.Range(0, 4).Select(i => new FareLegFact(i, null, FareCondition.NotProvided)),
            facts
        );
        result.IsError.ShouldBeFalse();
        facts[0] = facts[0] with { CheckedBaggageQuantity = 99 };
        counts[0] = 99;
        result.Value.Passengers[0].CheckedBaggageQuantity.ShouldBeNull();
        result.Value.SegmentCounts[0].ShouldBe(16);
        result.Value.Passengers.Count.ShouldBe(576);
    }

    [Fact]
    public void Semantic_key_ignores_observation_and_reorders_equal_scopes_but_keeps_facts()
    {
        var facts = new[]
        {
            new FareSegmentFact(0, 0, 0, "Y", null, 0, null),
            new FareSegmentFact(0, 0, 1, "B", null, null, 1),
        };
        var a = Evidence(facts).Value;
        var b = Evidence(facts.Reverse(), Now.AddMinutes(1)).Value;
        a.SemanticKey().ShouldBe(b.SemanticKey());
        a.SemanticKey()
            .ShouldNotBe(
                Evidence(new[] { facts[0] with { CheckedBaggageQuantity = null }, facts[1] })
                    .Value.SemanticKey()
            );
        var swapped = a.RemapAdults(new[] { 1, 0 });
        swapped.IsError.ShouldBeFalse();
        swapped.Value.Passengers[0].Adult.ShouldBe(1);
        a.RemapAdults(new[] { 0, 0 }).IsError.ShouldBeTrue();
        JsonSerializer.Deserialize<FareRuleEvidence>(JsonSerializer.Serialize(a)).ShouldBe(a);
    }

    [Fact]
    public void Frozen_snapshot_maps_adults_roundtrips_and_is_cleared_by_incomplete_requote()
    {
        var party = BookableOfferParty
            .Create(
                new[] { Slot("adult-a"), Slot("adult-b") },
                new DateOnly(2026, 10, 13),
                true,
                false
            )
            .Value;
        var binding = QuoteBinding
            .Create(Guid.NewGuid(), party, new[] { QuoteSlot("adult-b"), QuoteSlot("adult-a") })
            .Value;
        var segment = Segment
            .Create(
                IataCode.Create("AAA").Value,
                IataCode.Create("BBB").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                "XX",
                "100",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary.Create(new[] { Slice.Create(new[] { segment }).Value }).Value;
        var amount = Money.Create(100m, CurrencyCode.Create("EUR").Value).Value;
        var evidence = Evidence(
            new[]
            {
                new FareSegmentFact(0, 0, 0, "A", null, 0, null),
                new FareSegmentFact(0, 0, 1, "B", null, null, 1),
            }
        ).Value;
        var offer = new BookableOffer(
            OfferId.New(),
            itinerary,
            amount,
            ProviderId.Duffel,
            Now,
            Now.AddHours(1),
            new FareConditions(false, false, null, null),
            "fictional-offer",
            party,
            evidence
        );
        var snapshot = QuotedFareSnapshot.Create(Guid.NewGuid(), offer, binding).Value;
        snapshot.Fingerprint.Length.ShouldBe(64);
        snapshot.Evidence.Passengers[0].Adult.ShouldBe(1);
        var quoted = new OfferQuoted(
            offer.Id,
            itinerary,
            amount,
            offer.ExpiresAt,
            offer.ProviderOfferRef,
            Now,
            offer.FareConditions,
            binding,
            snapshot
        );
        var persisted = JsonSerializer.Deserialize<OfferQuoted>(JsonSerializer.Serialize(quoted))!;
        var aggregate = new BookingAggregate();
        aggregate.Apply(persisted);
        aggregate.FareSnapshot.ShouldBe(snapshot);
        var heldUntil = Now.AddHours(12);
        aggregate.Apply(
            new OfferHeldV3(
                "fictional-held-order",
                ProtectedPassengerPartySnapshot.Create(1, "opaque-no-key").Value,
                heldUntil,
                Now.AddMinutes(1),
                Guid.NewGuid(),
                binding.Revision,
                2
            )
        );
        aggregate.ExpiresAt.ShouldBe(heldUntil);
        aggregate.FareSnapshot.ShouldBe(snapshot);
        aggregate.FareSnapshot!.Evidence.OfferExpiresAt.ShouldBe(offer.ExpiresAt);
        aggregate.FareSnapshot.Fingerprint.ShouldBe(snapshot.Fingerprint);
        aggregate.Apply(new OfferReQuoted(offer.Id, amount, amount, Now.AddMinutes(1)));
        aggregate.FareSnapshot.ShouldBeNull();
        aggregate.Apply(
            quoted with
            {
                QuoteBinding = QuoteBinding.Create(Guid.NewGuid(), party, binding.Slots).Value,
            }
        );
        aggregate.FareSnapshot.ShouldBeNull();
        var changed = QuotedFareSnapshot
            .Create(
                snapshot.AggregateId,
                offer with
                {
                    ProviderOfferRef = "different-fictional-offer",
                },
                binding
            )
            .Value;
        changed.Fingerprint.ShouldNotBe(snapshot.Fingerprint);
        JsonSerializer
            .Deserialize<QuotedFareSnapshot>(JsonSerializer.Serialize(snapshot))
            .ShouldBe(snapshot);
        var historicalJson = JsonSerializer
            .Serialize(snapshot)
            .Replace("\"FingerprintVersion\":1", "\"FingerprintVersion\":7")
            .Replace("\"SchemaVersion\":1", "\"SchemaVersion\":7");
        var historical = JsonSerializer.Deserialize<QuotedFareSnapshot>(historicalJson)!;
        historical.FingerprintVersion.ShouldBe(7);
        historical.Evidence.SchemaVersion.ShouldBe(7);
        historical.Fingerprint.ShouldBe(snapshot.Fingerprint);
        aggregate.Apply(quoted with { FareSnapshot = historical });
        aggregate.FareSnapshot.ShouldBe(historical);
        aggregate.Apply(quoted with { FareSnapshot = null });
        aggregate.FareSnapshot.ShouldBeNull();
    }

    private static SupplierPassengerSlot Slot(string reference) =>
        new(SupplierPassengerReference.Create(reference).Value, BookingPassengerKind.Adult);

    private static QuotePassengerSlot QuoteSlot(string reference) =>
        new(
            new BookingPassengerId(Guid.NewGuid()),
            SupplierPassengerReference.Create(reference).Value,
            BookingPassengerKind.Adult
        );

    private static ErrorOr.ErrorOr<FareRuleEvidence> Evidence(
        IEnumerable<FareSegmentFact> facts,
        DateTimeOffset? observed = null
    ) =>
        FareRuleEvidence.Create(
            observed ?? Now,
            Now.AddHours(1),
            Now.AddDays(1),
            2,
            new[] { 1 },
            FareCondition.NotProvided,
            FareCondition.NotProvided,
            new[] { new FareLegFact(0, null, FareCondition.NotProvided) },
            facts
        );
}
