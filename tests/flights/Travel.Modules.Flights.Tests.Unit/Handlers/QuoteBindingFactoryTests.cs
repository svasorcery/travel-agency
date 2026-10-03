using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Handlers;

public sealed class QuoteBindingFactoryTests
{
    private static BookableOfferParty Party(params string[] refs) =>
        BookableOfferParty
            .Create(
                refs.Select(r => new SupplierPassengerSlot(
                    SupplierPassengerReference.Create(r).Value,
                    BookingPassengerKind.Adult
                )),
                new DateOnly(2027, 1, 1),
                true,
                false
            )
            .Value;

    [Fact]
    public void Same_reference_set_preserves_old_ids_and_slot_order_but_changes_revision()
    {
        var old = QuoteBindingFactory.Create(Party("a", "b"), 2, null).Value;
        var next = QuoteBindingFactory.Create(Party("b", "a"), 2, old).Value;
        next.Revision.ShouldNotBe(old.Revision);
        next.Slots.ToArray().ShouldBe(old.Slots.ToArray());
    }

    [Fact]
    public void Changed_set_replaces_all_local_ids()
    {
        var old = QuoteBindingFactory.Create(Party("a", "b"), 2, null).Value;
        var next = QuoteBindingFactory.Create(Party("a", "c"), 2, old).Value;
        next.Slots.Any(s => old.Slots.Any(o => o.Id == s.Id)).ShouldBeFalse();
    }

    [Fact]
    public void Requested_or_existing_count_mismatch_rejected()
    {
        QuoteBindingFactory
            .Create(Party("a", "b"), 1, null)
            .FirstError.Code.ShouldBe("Flights.PassengerCountMismatch");
        var old = QuoteBindingFactory.Create(Party("a"), 1, null).Value;
        QuoteBindingFactory
            .Create(Party("a", "b"), 2, old)
            .FirstError.Code.ShouldBe("Flights.PassengerCountMismatch");
    }
}
