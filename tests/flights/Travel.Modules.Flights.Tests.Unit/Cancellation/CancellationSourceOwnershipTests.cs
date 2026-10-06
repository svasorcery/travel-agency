using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationSourceOwnershipTests
{
    [Fact]
    public void New_recorded_owner_cannot_acquire_old_operation_metadata_or_dispatch_authority()
    {
        var booking = CancellationDecisionTests.Prepared();
        var replacement = Guid.NewGuid();
        booking.Apply(
            new OfferHeldV3(
                "ord_fictional_1",
                ProtectedPassengerPartySnapshot.Create(1, "fictional-protected").Value,
                CancellationTestData.Now.AddDays(1),
                CancellationTestData.Now,
                replacement,
                Guid.NewGuid(),
                1
            )
        );
        var writer = typeof(PrepareCancellationHandler).Assembly.GetType(
            "Travel.Modules.Flights.Application.Cancellation.CancellationDecisionWriter"
        )!;
        var owned = writer.GetMethod("Owned", BindingFlags.NonPublic | BindingFlags.Static)!;
        ((bool)owned.Invoke(null, new object[] { booking, replacement })!).ShouldBeFalse();
        var require = writer.GetMethod(
            "RequireSource",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;
        Should
            .Throw<TargetInvocationException>(() => require.Invoke(null, new object[] { booking }))
            .InnerException.ShouldBeOfType<Travel.Modules.Flights.Application.Booking.BookingSourceOwnershipMissingException>();
    }
}
