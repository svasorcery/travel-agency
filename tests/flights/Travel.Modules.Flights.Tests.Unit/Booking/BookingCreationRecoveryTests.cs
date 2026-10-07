using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Tests.Unit.Ancillaries;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Booking;

public sealed class BookingCreationRecoveryTests
{
    [Theory]
    [InlineData(false, 150)]
    [InlineData(true, 180)]
    [InlineData(true, 400)]
    public async Task Missing_id_or_elapsed_deadline_never_reads_or_resends(bool known, int seconds)
    {
        var (booking, actual, now) = AncillaryConfirmationTests.Held(false);
        var attempt = booking.CurrentCreation!;
        if (known)
            booking.Apply(
                booking
                    .DecideCreationObservation(
                        attempt.Id,
                        attempt.Revision,
                        new(
                            BookingCreationOutcome.ManualReviewRequired,
                            null,
                            actual.ProviderOrderId,
                            true,
                            true,
                            "OrderUnproven",
                            now
                        ),
                        now
                    )
                    .Value.Event!
            );
        var provider = new Reads(actual);
        var events = new List<object>();
        await CheckBookingCreationHandler.Handle(
            new(booking.Id, attempt.Id),
            BookingCreationCommitTests.Session(booking, events, () => { }),
            BookingCreationCommitTests.Outbox([]),
            [provider],
            new Clock(now.AddSeconds(seconds)),
            TestContext.Current.CancellationToken
        );
        provider.Calls.ShouldBe(0);
        booking.CurrentCreation!.Outcome.ShouldBe(BookingCreationOutcome.ManualReviewRequired);
        booking.CurrentCreation.CreationCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Correlated_read_can_finish_once_and_terminal_redelivery_is_noop()
    {
        var (booking, actual, now) = AncillaryConfirmationTests.Held(false);
        var attempt = booking.CurrentCreation!;
        booking.Apply(
            booking
                .DecideCreationObservation(
                    attempt.Id,
                    attempt.Revision,
                    new(
                        BookingCreationOutcome.ManualReviewRequired,
                        null,
                        actual.ProviderOrderId,
                        true,
                        true,
                        "OrderUnproven",
                        now
                    ),
                    now
                )
                .Value.Event!
        );
        var provider = new Reads(actual);
        var events = new List<object>();
        var clock = new Clock(now.AddSeconds(150));
        var session = BookingCreationCommitTests.Session(booking, events, () => { });
        await CheckBookingCreationHandler.Handle(
            new(booking.Id, attempt.Id),
            session,
            BookingCreationCommitTests.Outbox([]),
            [provider],
            clock,
            TestContext.Current.CancellationToken
        );
        booking.CurrentCreation!.Outcome.ShouldBe(BookingCreationOutcome.Matches);
        booking.CurrentCreation.CreationCompleted.ShouldBeTrue();
        await CheckBookingCreationHandler.Handle(
            new(booking.Id, attempt.Id),
            session,
            BookingCreationCommitTests.Outbox([]),
            [provider],
            clock,
            TestContext.Current.CancellationToken
        );
        provider.Calls.ShouldBe(1);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Reads(BookedOrderFacts actual) : IFlightBookingProvider
    {
        public int Calls;
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookedOrderFacts>> ReadOrderForBookingAsync(
            string id,
            BookableOffer offer,
            QuoteBinding binding,
            BookingPurchase purchase,
            Guid attempt,
            CancellationToken ct
        )
        {
            id.ShouldBe(actual.ProviderOrderId);
            Calls++;
            return Task.FromResult<ErrorOr<BookedOrderFacts>>(actual);
        }

        public Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            BookingPurchase purchase,
            Guid attemptId,
            CancellationToken ct
        ) => throw new InvalidOperationException("Recovery cannot create an order.");

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string reference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string order,
            Money total,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string order,
            PaymentRef payment,
            Money total,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
