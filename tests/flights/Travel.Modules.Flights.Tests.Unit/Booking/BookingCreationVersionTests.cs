using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Tests.Unit.Ancillaries;

namespace Travel.Modules.Flights.Tests.Unit.Booking;

public sealed class BookingCreationVersionTests
{
    [Fact]
    public async Task Equal_version_read_can_expose_the_later_time_diagnosis_without_mutating_source()
    {
        var (booking, _, now) = AncillaryConfirmationTests.Held(false);
        var time = new FakeTimeProvider(now.AddSeconds(179));
        var session = BookingCreationCommitTests.Session(
            booking,
            [],
            () => throw new InvalidOperationException("Read must not save")
        );
        var query = new GetBookingCreationQuery(booking.Id, booking.OwnerUserId!.Value);
        var before = (
            await GetBookingCreationHandler.Handle(
                query,
                session,
                time,
                TestContext.Current.CancellationToken
            )
        ).Value;
        time.Advance(TimeSpan.FromSeconds(1));
        var after = (
            await GetBookingCreationHandler.Handle(
                query,
                session,
                time,
                TestContext.Current.CancellationToken
            )
        ).Value;
        after.BookingVersion.ShouldBe(before.BookingVersion);
        BookingCreationResponse.From(before).State.ShouldBe("InProgress");
        BookingCreationResponse.From(after).State.ShouldBe("ManualReviewRequired");
        booking.CurrentCreation!.Outcome.ToString().ShouldBe("InProgress");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Source_query_uses_the_owned_stream_snapshot_without_appending_or_saving(
        bool owned
    )
    {
        var owner = Guid.NewGuid();
        var booking = new BookingAggregate();
        booking.Apply(
            new OfferHeld(
                "ord_fictional",
                null!,
                DateTimeOffset.UnixEpoch.AddHours(1),
                DateTimeOffset.UnixEpoch,
                owner
            )
        );
        var appended = new List<object>();
        var session = BookingCreationCommitTests.Session(
            booking,
            appended,
            () => throw new InvalidOperationException("Read must not save")
        );
        var value = await GetBookingCreationHandler.Handle(
            new GetBookingCreationQuery(Guid.NewGuid(), owned ? owner : Guid.NewGuid()),
            session,
            TimeProvider.System,
            TestContext.Current.CancellationToken
        );
        value.IsError.ShouldBe(!owned);
        if (owned)
            value.Value.BookingVersion.ShouldBe(2);
        appended.ShouldBeEmpty();
    }
}
