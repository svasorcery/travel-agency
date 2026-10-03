using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class PassengerPartyReplayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Predecrypt_hold_checks_revision_count_and_capabilities()
    {
        var binding = Binding();
        var booking = Quoted(binding);
        booking
            .DecideHold(Now)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.QuoteBindingRequired);
        booking
            .DecideHold(Now, Guid.NewGuid(), 1)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.QuoteRevisionMismatch);
        booking
            .DecideHold(Now, binding.Revision, 2)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.PassengerCountMismatch);
        booking
            .DecideHold(Now, binding.Revision, 1)
            .ShouldBeOfType<BookingTransitionDecision.Allowed>();
        var unsupported = Binding(hold: null);
        Quoted(unsupported)
            .DecideHold(Now, unsupported.Revision, 1)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.HoldNotSupported);
        var docs = Binding(documents: null);
        Quoted(docs)
            .DecideHold(Now, docs.Revision, 1)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.IdentityDocumentsRequired);
    }

    [Fact]
    public void Requote_without_refreshed_offer_replaces_or_clears_binding_before_early_return()
    {
        var first = Binding();
        var second = Binding();
        var booking = Quoted(first);
        booking.Apply(
            new OfferReQuoted(
                OfferId.New(),
                booking.TotalAmount!,
                booking.TotalAmount!,
                Now,
                QuoteBinding: second
            )
        );
        booking.QuoteBinding.ShouldBe(second);
        booking.Apply(
            new OfferReQuoted(OfferId.New(), booking.TotalAmount!, booking.TotalAmount!, Now)
        );
        booking.QuoteBinding.ShouldBeNull();
        booking
            .DecideHold(Now, first.Revision, 1)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>()
            .Reason.Code.ShouldBe(BookingRejectionCode.QuoteBindingRequired);
    }

    [Fact]
    public void V3_json_replay_copies_exact_ciphertext_count_revision_and_legacy_versions_reset_party()
    {
        var owner = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var snapshot = ProtectedPassengerPartySnapshot.Create(1, "opaque-no-key-needed").Value;
        var source = new OfferHeldV3(
            "ord_party",
            snapshot,
            Now.AddHours(1),
            Now,
            owner,
            revision,
            9
        );
        var restored = JsonSerializer.Deserialize<OfferHeldV3>(JsonSerializer.Serialize(source))!;
        var booking = new BookingAggregate();
        booking.Apply(restored);
        booking.ProtectedPassengerParty.ShouldBe(snapshot);
        booking.PassengerCount.ShouldBe(9);
        booking.HeldQuoteRevision.ShouldBe(revision);
        booking.OwnerUserId.ShouldBe(owner);
        booking.BookedAt.ShouldBe(Now);
        booking.Passenger.ShouldBeNull();
        booking.ProtectedPassenger.ShouldBeNull();
        booking.DecideConfirm(Now).ShouldBeOfType<BookingTransitionDecision.Allowed>();
        booking.Apply(
            new OfferHeldV2(
                "ord_v2",
                ProtectedPassengerSnapshot.Create(1, "old").Value,
                Now.AddHours(1),
                Now,
                owner
            )
        );
        booking.PassengerCount.ShouldBe(1);
        booking.HeldQuoteRevision.ShouldBeNull();
        booking.ProtectedPassengerParty.ShouldBeNull();
        booking.Apply(source);
        booking.Apply(
            new OfferHeld("ord_v1", ValueObjects.PassengerPartyTests.Info(), Now.AddHours(1), Now)
        );
        booking.PassengerCount.ShouldBe(1);
        booking.HeldQuoteRevision.ShouldBeNull();
        booking.ProtectedPassengerParty.ShouldBeNull();
    }

    [Fact]
    public void Historical_offer_json_omission_deserializes_without_inventing_binding()
    {
        var quoted = Quoted(null);
        quoted.QuoteBinding.ShouldBeNull();
        quoted.PassengerCount.ShouldBe(1);
        var historical = JsonNode.Parse(JsonSerializer.Serialize(Quote(null)))!.AsObject();
        historical.Remove("QuoteBinding");
        var json = historical.ToJsonString();
        var restored = JsonSerializer.Deserialize<OfferQuoted>(json)!;
        restored.QuoteBinding.ShouldBeNull();
        var binding = Binding();
        var replay = JsonSerializer.Deserialize<OfferQuoted>(
            JsonSerializer.Serialize(Quote(binding))
        )!;
        var aggregate = new BookingAggregate();
        aggregate.Apply(replay);
        aggregate.QuoteBinding.ShouldBe(binding);
    }

    internal static QuoteBinding Binding(bool? hold = true, bool? documents = false)
    {
        var reference = SupplierPassengerReference.Create("ref_test").Value;
        var party = BookableOfferParty
            .Create([new(reference, BookingPassengerKind.Adult)], new(2030, 1, 1), hold, documents)
            .Value;
        return QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new(
                        BookingPassengerId.Create(Guid.NewGuid()).Value,
                        reference,
                        BookingPassengerKind.Adult
                    ),
                ]
            )
            .Value;
    }

    internal static BookingAggregate Quoted(QuoteBinding? binding)
    {
        var aggregate = new BookingAggregate();
        aggregate.Apply(Quote(binding));
        return aggregate;
    }

    private static OfferQuoted Quote(QuoteBinding? binding)
    {
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(1),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return new(
            OfferId.New(),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            Money.Create(100, CurrencyCode.Create("USD").Value).Value,
            Now.AddMinutes(30),
            "off_test",
            Now,
            QuoteBinding: binding
        );
    }
}
