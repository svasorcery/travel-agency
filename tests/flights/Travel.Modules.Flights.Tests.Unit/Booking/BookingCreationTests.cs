using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Booking;

public sealed class BookingCreationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid Request = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid Sender = Guid.Parse("00000000-0000-0000-0000-000000000030");
    private const string Digest =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Saved_start_blocks_another_key_and_every_requote_writer()
    {
        var booking = Quoted();
        var start = Start(booking);
        start.IsError.ShouldBeFalse();
        start.Value.IsNew.ShouldBeTrue();
        booking.Apply(start.Value.Event!);
        Start(booking, Guid.NewGuid()).IsError.ShouldBeTrue();
        booking.DecideReQuote("off_fictional").ShouldBeOfType<BookingTransitionDecision.Rejected>();
        booking
            .DecideHold(Now, booking.QuoteBinding!.Revision, 1)
            .ShouldBeOfType<BookingTransitionDecision.Rejected>();
        booking.CurrentCreation!.OwnerId.ShouldBe(Owner);
    }

    [Fact]
    public void Exact_receipt_replay_precedes_expiry_and_new_revision_guards()
    {
        var booking = Quoted();
        var start = Start(booking);
        start.IsError.ShouldBeFalse();
        booking.Apply(start.Value.Event!);
        var repeat = booking.DecideCreationStart(
            Owner,
            Request,
            Digest,
            start.Value.Event!.QuoteRevision,
            ProtectedPassengerPartySnapshot.Create(1, "different-random-ciphertext").Value,
            Sender,
            Now.AddDays(1),
            false
        );
        repeat.IsError.ShouldBeFalse();
        repeat.Value.IsNew.ShouldBeFalse();
        repeat.Value.AttemptId.ShouldBe(Request);
        repeat.Value.Event.ShouldBeNull();
    }

    [Fact]
    public void Same_key_changed_body_and_foreign_owner_do_not_replay()
    {
        var booking = Quoted();
        var start = Start(booking);
        start.IsError.ShouldBeFalse();
        booking.Apply(start.Value.Event!);
        var changed = booking.DecideCreationStart(
            Owner,
            Request,
            new string('b', 64),
            booking.QuoteBinding!.Revision,
            Snapshot(),
            Sender,
            Now,
            false
        );
        changed.IsError.ShouldBeTrue();
        changed.FirstError.Code.ShouldBe("Flights.IdempotencyConflict");
        booking
            .DecideCreationStart(
                Guid.NewGuid(),
                Request,
                Digest,
                booking.QuoteBinding.Revision,
                Snapshot(),
                Sender,
                Now,
                false
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Restart_replays_barrier_without_decrypting_or_resending()
    {
        var first = Quoted();
        var start = Start(first);
        start.IsError.ShouldBeFalse();
        var saved = System.Text.Json.JsonSerializer.Serialize(start.Value.Event);
        var restarted = Quoted(first.QuoteBinding!.Revision);
        restarted.Apply(
            System.Text.Json.JsonSerializer.Deserialize<BookingCreationStarted>(saved)!
        );
        Start(restarted).Value.IsNew.ShouldBeFalse();
        restarted.CurrentCreation!.ProtectedParty.Ciphertext.ShouldBe("opaque-fictional-party");
        restarted.HasCreationBarrier.ShouldBeTrue();
    }

    private static ErrorOr.ErrorOr<CreationAdmission> Start(
        BookingAggregate booking,
        Guid? key = null
    ) =>
        booking.DecideCreationStart(
            Owner,
            key ?? Request,
            Digest,
            booking.QuoteBinding!.Revision,
            Snapshot(),
            Sender,
            Now,
            false
        );

    [Fact]
    public void Known_order_with_differences_is_created_but_cannot_be_confirmed()
    {
        var booking = Quoted();
        booking.Apply(Start(booking).Value.Event!);
        var actual = Actual(booking) with
        {
            Total = Money.Create(60, CurrencyCode.Create("GBP").Value).Value,
        };
        var decision = booking.DecideCreationObservation(
            Request,
            1,
            new(
                BookingCreationOutcome.CreatedWithDifferences,
                actual,
                actual.ProviderOrderId,
                true,
                true,
                "ServicesChanged",
                Now
            ),
            Now
        );
        decision.IsError.ShouldBeFalse();
        booking.Apply(decision.Value.Event!);
        booking.Apply(
            new OfferHeldV3(
                actual.ProviderOrderId,
                Snapshot(),
                actual.PaymentRequiredBy,
                Now,
                Owner,
                booking.QuoteBinding!.Revision,
                1
            )
        );
        booking.CurrentCreation!.CreationCompleted.ShouldBeTrue();
        booking.HasUnresolvedCreation.ShouldBeFalse();
        booking.DecideConfirm(Now).ShouldBeOfType<BookingTransitionDecision.Rejected>();
        booking.DecideCancel().ShouldBeOfType<BookingTransitionDecision.Allowed>();
        booking.TotalAmount!.Amount.ShouldBe(60);
    }

    [Fact]
    public void Unknown_cannot_be_erased_by_later_supplier_no_create_reply()
    {
        var booking = Quoted();
        booking.Apply(Start(booking).Value.Event!);
        var unknown = booking.DecideCreationObservation(
            Request,
            1,
            new(
                BookingCreationOutcome.ManualReviewRequired,
                null,
                null,
                false,
                false,
                "LostResponse",
                Now
            ),
            Now
        );
        unknown.IsError.ShouldBeFalse();
        booking.Apply(unknown.Value.Event!);
        var negative = booking.DecideCreationObservation(
            Request,
            booking.CurrentCreation!.Revision,
            new(
                BookingCreationOutcome.NotCreated,
                null,
                null,
                false,
                true,
                "OfferExpired",
                Now.AddSeconds(1),
                PositiveNoEffects: true
            ),
            Now.AddSeconds(1)
        );
        negative.IsError.ShouldBeTrue();
        booking.HasUnresolvedCreation.ShouldBeTrue();
    }

    [Fact]
    public void Positive_no_create_allows_requote_but_not_another_create_from_the_old_quote()
    {
        var booking = Quoted();
        booking.Apply(Start(booking).Value.Event!);
        var noCreation = booking.DecideCreationObservation(
            Request,
            1,
            new(
                BookingCreationOutcome.NotCreated,
                null,
                null,
                false,
                true,
                "SupplierRejected",
                Now,
                PositiveNoEffects: true
            ),
            Now
        );
        booking.Apply(noCreation.Value.Event!);
        Start(booking).Value.IsNew.ShouldBeFalse();
        var retry = Start(booking, Guid.NewGuid());
        retry.IsError.ShouldBeTrue();
        retry.FirstError.Code.ShouldBe("Flights.QuoteRevisionMismatch");
        booking.DecideReQuote("off_fictional").ShouldBeOfType<BookingTransitionDecision.Allowed>();
        var oldBinding = booking.QuoteBinding!;
        var binding = QuoteBinding.Create(Guid.NewGuid(), oldBinding.Party, oldBinding.Slots).Value;
        booking.Apply(
            new OfferReQuoted(
                booking.OfferId!.Value,
                booking.TotalAmount!,
                booking.TotalAmount!,
                Now,
                QuoteBinding: binding
            )
        );
        booking.Apply(
            new BookingPurchaseQuoted(
                BookingPurchase
                    .Create(
                        binding.Revision,
                        Owner,
                        booking.TotalAmount!,
                        booking.ExpiresAt!.Value,
                        []
                    )
                    .Value,
                Now
            )
        );
        Start(booking, Guid.NewGuid()).Value.IsNew.ShouldBeTrue();
        booking.CreationAttempts.ContainsKey(Request).ShouldBeTrue();
    }

    [Fact]
    public void Only_matched_complete_order_releases_confirmation_and_duplicates_emit_nothing()
    {
        var booking = Quoted();
        booking.Apply(Start(booking).Value.Event!);
        var actual = Actual(booking);
        var observation = new BookingCreationObservation(
            BookingCreationOutcome.Matches,
            actual,
            actual.ProviderOrderId,
            true,
            true,
            "",
            Now
        );
        var decision = booking.DecideCreationObservation(Request, 1, observation, Now);
        decision.IsError.ShouldBeFalse();
        booking.Apply(decision.Value.Event!);
        booking.Apply(
            new OfferHeldV3(
                actual.ProviderOrderId,
                Snapshot(),
                actual.PaymentRequiredBy,
                Now,
                Owner,
                booking.QuoteBinding!.Revision,
                1
            )
        );
        booking.DecideConfirm(Now).ShouldBeOfType<BookingTransitionDecision.Allowed>();
        var duplicate = booking.DecideCreationObservation(
            Request,
            booking.CurrentCreation!.Revision,
            observation,
            Now
        );
        duplicate.IsError.ShouldBeFalse();
        duplicate.Value.Changed.ShouldBeFalse();
        duplicate.Value.Event.ShouldBeNull();
    }

    private static BookedOrderFacts Actual(BookingAggregate booking) =>
        new(
            "ord_fictional",
            "off_fictional",
            booking.Itinerary!,
            new(booking.QuoteBinding!.Slots.Select(s => s.Id.Value).ToArray()),
            new([]),
            booking.TotalAmount!,
            true,
            Now.AddHours(1)
        );

    private static ProtectedPassengerPartySnapshot Snapshot() =>
        ProtectedPassengerPartySnapshot.Create(1, "opaque-fictional-party").Value;

    internal static BookingAggregate Quoted(Guid? revision = null)
    {
        var reference = SupplierPassengerReference.Create("pas_fictional").Value;
        var party = BookableOfferParty
            .Create(
                [new(reference, BookingPassengerKind.Adult)],
                new DateOnly(2030, 1, 2),
                true,
                false
            )
            .Value;
        var binding = QuoteBinding
            .Create(
                revision ?? Guid.NewGuid(),
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
        var segment = Segment
            .Create(
                IataCode.Create("LHR").Value,
                IataCode.Create("CDG").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(2),
                "ZZ",
                "101",
                CabinClass.Parse("economy").Value
            )
            .Value;
        var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
        var booking = new BookingAggregate();
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(booking, Guid.Parse("00000000-0000-0000-0000-000000000040"));
        booking.Apply(
            new OfferQuoted(
                OfferId.New(),
                itinerary,
                Money.Create(50m, CurrencyCode.Create("GBP").Value).Value,
                Now.AddMinutes(10),
                "off_fictional",
                Now,
                QuoteBinding: binding
            )
        );
        return booking;
    }
}
