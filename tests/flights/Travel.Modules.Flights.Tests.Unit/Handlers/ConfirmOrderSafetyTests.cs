using System.Reflection;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Tests.Unit.Handlers;

public sealed class ConfirmOrderSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money Total = Money
        .Create(250m, CurrencyCode.Create("USD").Value)
        .Value;

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task V3_party_confirms_one_accepted_group_total_with_one_payment(int count)
    {
        var owner = Guid.NewGuid();
        var binding = Binding(count);
        var aggregate = new BookingAggregate();
        aggregate.Apply(
            new OfferQuoted(
                OfferId.New(),
                null!,
                Total,
                Now.AddHours(1),
                "off_group",
                Now,
                QuoteBinding: binding
            )
        );
        aggregate.Apply(
            new OfferHeldV3(
                "ord_group",
                ProtectedPassengerPartySnapshot.Create(1, "opaque-held-party").Value,
                Now.AddHours(1),
                Now,
                owner,
                binding.Revision,
                count
            )
        );
        aggregate.Apply(new BookingMutationCoordinationEnabled(Now));
        var events = new List<object>();
        var messages = new List<object>();
        var saves = 0;
        var provider = new Provider("success");
        var wallet = new Wallet("success");
        var result = await ConfirmOrderHandler.Handle(
            new(Guid.NewGuid(), owner),
            Session(aggregate, events, () => saves++),
            [provider],
            wallet,
            Metrics(),
            Outbox(messages),
            new FakeTimeProvider(Now),
            NullLogger<ConfirmOrderCommand>.Instance,
            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        wallet.Authorizations.ShouldBe(1);
        wallet.Accepted.ShouldBe(Total);
        provider.Confirms.ShouldBe(1);
        provider.Accepted.ShouldBe(Total);
        saves.ShouldBe(5);
        events
            .Select(e => e.GetType())
            .ShouldBe([
                typeof(ConfirmationAttemptStarted),
                typeof(ConfirmationEffectsClaimed),
                typeof(ConfirmationPaymentReferenceRecorded),
                typeof(ConfirmationCaptureObserved),
                typeof(ConfirmationAttemptCompleted),
                typeof(PaymentAuthorized),
                typeof(OrderConfirmed),
            ]);
    }

    [Theory]
    [InlineData("preflight")]
    [InlineData("capture")]
    [InlineData("provider")]
    [InlineData("provider-price")]
    [InlineData("provider-expired")]
    [InlineData("capture-throw")]
    [InlineData("provider-throw")]
    public async Task Refused_or_uncertain_confirmation_keeps_held_stream_without_compensation(
        string failure
    )
    {
        var owner = Guid.NewGuid();
        var aggregate = Held(owner);
        aggregate.Apply(new BookingMutationCoordinationEnabled(Now));
        var events = new List<object>();
        var messages = new List<object>();
        var saves = 0;
        var session = Session(aggregate, events, () => saves++);
        var provider = new Provider(failure);
        var gateway = new Wallet(failure);
        var result = await ConfirmOrderHandler.Handle(
            new(Guid.NewGuid(), owner),
            session,
            [provider],
            gateway,
            Metrics(),
            Outbox(messages),
            new FakeTimeProvider(Now),
            NullLogger<ConfirmOrderCommand>.Instance,
            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(
            failure == "preflight"
                ? "Flights.OrderPriceChanged"
                : "Flights.ConfirmationOutcomeUnknown"
        );
        result.FirstError.Description.ShouldNotContain("private");
        events.OfType<PaymentAuthorized>().ShouldBeEmpty();
        events.OfType<OrderConfirmed>().ShouldBeEmpty();
        events.OfType<ConfirmationAttemptStarted>().ShouldHaveSingleItem();
        messages
            .OfType<Travel.Modules.Flights.Application.Cancellation.ConfirmationBarrierDeadline>()
            .ShouldHaveSingleItem();
        saves.ShouldBe(
            failure == "preflight" ? 2
            : failure.StartsWith("provider", StringComparison.Ordinal) ? 5
            : 4
        );
        aggregate.CurrentConfirmationAttempt!.Phase.ShouldBe(
            failure == "preflight"
                ? Travel.Modules.Flights.Core.Cancellation.ConfirmationAttemptPhase.NotDispatched
                : Travel
                    .Modules
                    .Flights
                    .Core
                    .Cancellation
                    .ConfirmationAttemptPhase
                    .ManualReviewRequired
        );
        aggregate.Status.ShouldBe(BookingStatus.Held);
        gateway.Refunds.ShouldBe(0);
        provider.Cancels.ShouldBe(0);
        gateway.Authorizations.ShouldBe(failure == "preflight" ? 0 : 1);
        provider.Confirms.ShouldBe(
            failure.StartsWith("provider", StringComparison.Ordinal) ? 1 : 0
        );
    }

    [Fact]
    public async Task Successful_confirmation_preserves_existing_events_atomic_save_and_notification()
    {
        var owner = Guid.NewGuid();
        var aggregate = Held(owner);
        aggregate.Apply(new BookingMutationCoordinationEnabled(Now));
        var events = new List<object>();
        var messages = new List<object>();
        var saves = 0;
        var provider = new Provider("success");
        var result = await ConfirmOrderHandler.Handle(
            new(Guid.NewGuid(), owner),
            Session(aggregate, events, () => saves++),
            [provider],
            new Wallet("success"),
            Metrics(),
            Outbox(messages),
            new FakeTimeProvider(Now),
            NullLogger<ConfirmOrderCommand>.Instance,
            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        events
            .Select(e => e.GetType())
            .ShouldBe([
                typeof(ConfirmationAttemptStarted),
                typeof(ConfirmationEffectsClaimed),
                typeof(ConfirmationPaymentReferenceRecorded),
                typeof(ConfirmationCaptureObserved),
                typeof(ConfirmationAttemptCompleted),
                typeof(PaymentAuthorized),
                typeof(OrderConfirmed),
            ]);
        saves.ShouldBe(5);
        messages
            .OfType<Travel.Modules.Flights.Application.Contracts.OrderConfirmedNotification>()
            .ShouldHaveSingleItem()
            .RequiredStreamVersion.ShouldBe(9);
        messages
            .OfType<Travel.Modules.Flights.Application.ReadModels.ReconcileOrderReadModel>()
            .Count()
            .ShouldBe(5);
        messages
            .OfType<Travel.Modules.Flights.Application.Cancellation.ConfirmationBarrierDeadline>()
            .ShouldHaveSingleItem();
        provider.Accepted.ShouldBe(Total);
    }

    [Theory]
    [InlineData("capture", false)]
    [InlineData("capture", true)]
    [InlineData("provider", false)]
    [InlineData("provider", true)]
    public async Task Cancellation_keeps_actual_token_and_sanitizes_chain_without_booking_effects(
        string step,
        bool taskCancelled
    )
    {
        using var cts = new CancellationTokenSource();
        OperationCanceledException cancellation = taskCancelled
            ? new TaskCanceledException(
                "private-confirmation@example.test",
                new IOException("private-body"),
                cts.Token
            )
            : new OperationCanceledException(
                "private-confirmation@example.test",
                new IOException("private-body"),
                cts.Token
            );
        var owner = Guid.NewGuid();
        var events = new List<object>();
        var messages = new List<object>();
        var saves = 0;
        var provider = new Provider("success", step == "provider" ? cancellation : null);
        var wallet = new Wallet("success", step == "capture" ? cancellation : null);
        var pending = ConfirmOrderHandler.Handle(
            new(Guid.NewGuid(), owner),
            Session(Held(owner), events, () => saves++),
            [provider],
            wallet,
            Metrics(),
            Outbox(messages),
            new FakeTimeProvider(Now),
            NullLogger<ConfirmOrderCommand>.Instance,
            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
            cts.Token
        );
        OperationCanceledException? error = null;
        try
        {
            await pending;
        }
        catch (OperationCanceledException caught)
        {
            error = caught;
        }
        error.ShouldNotBeNull();
        pending.IsCanceled.ShouldBeTrue();
        error.CancellationToken.ShouldBe(cts.Token);
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("private-confirmation");
        error.ToString().ShouldNotContain("private-body");
        events.OfType<PaymentAuthorized>().ShouldBeEmpty();
        events.OfType<OrderConfirmed>().ShouldBeEmpty();
        events.OfType<ConfirmationAttemptStarted>().ShouldHaveSingleItem();
        messages
            .OfType<Travel.Modules.Flights.Application.Cancellation.ConfirmationBarrierDeadline>()
            .ShouldHaveSingleItem();
        saves.ShouldBe(step == "capture" ? 3 : 4);
        provider.Cancels.ShouldBe(0);
        wallet.Refunds.ShouldBe(0);
    }

    private static BookingAggregate Held(Guid owner)
    {
        var agg = new BookingAggregate();
        agg.Apply(new OfferQuoted(OfferId.New(), null!, Total, Now.AddHours(1), "off_test", Now));
        agg.Apply(new OfferHeld("ord_test", null!, Now.AddHours(1), Now, owner));
        agg.Apply(new BookingMutationCoordinationEnabled(Now));
        return agg;
    }

    private static IDocumentSession Session(
        BookingAggregate aggregate,
        List<object> events,
        Action save
    )
    {
        var store = Proxy.Create(
            typeof(IDocumentStore),
            (method, _) =>
            {
                if (method.Name == "LightweightSession")
                    return Session(aggregate, events, save);
                throw new InvalidOperationException(method.Name);
            }
        );
        return (IDocumentSession)
            Proxy.Create(
                typeof(IDocumentSession),
                (method, args) =>
                {
                    if (method.Name == "get_DocumentStore")
                        return store;
                    if (method.Name == "DisposeAsync")
                        return ValueTask.CompletedTask;
                    if (method.Name == "Dispose")
                        return null;
                    if (method.Name == "get_Events")
                        return Proxy.Create(
                            method.ReturnType,
                            (eventMethod, eventArgs) =>
                            {
                                if (eventMethod.Name != "FetchForWriting")
                                    throw new InvalidOperationException(eventMethod.Name);
                                typeof(BookingAggregate)
                                    .GetProperty(nameof(BookingAggregate.Id))!
                                    .SetValue(aggregate, eventArgs![0]);
                                typeof(BookingAggregate)
                                    .GetProperty(nameof(BookingAggregate.Version))!
                                    .SetValue(aggregate, 2 + events.Count);
                                var streamType = eventMethod.ReturnType.GetGenericArguments()[0];
                                var stream = Proxy.Create(
                                    streamType,
                                    (streamMethod, streamArgs) =>
                                        streamMethod.Name switch
                                        {
                                            "get_Aggregate" => aggregate,
                                            "get_CurrentVersion" => 2L + events.Count,
                                            "AppendOne" => Record(events, streamArgs![0]!),
                                            _ => throw new InvalidOperationException(
                                                streamMethod.Name
                                            ),
                                        }
                                );
                                return typeof(Task)
                                    .GetMethod(nameof(Task.FromResult))!
                                    .MakeGenericMethod(streamType)
                                    .Invoke(null, [stream]);
                            }
                        );
                    if (method.Name == "SaveChangesAsync")
                    {
                        save();
                        return Task.CompletedTask;
                    }
                    throw new InvalidOperationException(method.Name);
                }
            );
    }

    private static QuoteBinding Binding(int count)
    {
        var references = Enumerable
            .Range(0, count)
            .Select(i => SupplierPassengerReference.Create("fictional_" + i).Value)
            .ToArray();
        var party = BookableOfferParty
            .Create(
                references
                    .Select(reference => new SupplierPassengerSlot(
                        reference,
                        BookingPassengerKind.Adult
                    ))
                    .ToArray(),
                new DateOnly(2030, 1, 1),
                true,
                false
            )
            .Value;
        return QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                references.Select(reference => new QuotePassengerSlot(
                    BookingPassengerId.Create(Guid.NewGuid()).Value,
                    reference,
                    BookingPassengerKind.Adult
                ))
            )
            .Value;
    }

    private static object? Record(List<object> list, object value)
    {
        list.Add(value);
        return null;
    }

    private static IFlightsMetrics Metrics() =>
        (IFlightsMetrics)Proxy.Create(typeof(IFlightsMetrics), (_, _) => null);

    private static IMartenOutbox Outbox(List<object> messages) =>
        (IMartenOutbox)
            Proxy.Create(
                typeof(IMartenOutbox),
                (method, args) =>
                {
                    if (method.Name == "PublishAsync")
                        messages.Add(args![0]!);
                    if (method.ReturnType == typeof(ValueTask))
                        return ValueTask.CompletedTask;
                    return method.ReturnType == typeof(Task) ? Task.CompletedTask : null;
                }
            );

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static object Create(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = (Proxy)DispatchProxy.Create(type, typeof(Proxy));
            proxy.Handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Handler(targetMethod!, args);
    }

    private sealed class Provider(string failure, OperationCanceledException? cancellation = null)
        : IFlightBookingProvider
    {
        public int Confirms { get; private set; }
        public int Cancels { get; private set; }
        public Money? Accepted { get; private set; }
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<Success>>(
                failure == "preflight"
                    ? Error.Conflict("Flights.OrderPriceChanged", "Order price changed.")
                    : Result.Success
            );

        public async Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            Func<CancellationToken, Task<bool>> canDispatch,
            CancellationToken ct
        )
        {
            if (!await canDispatch(ct))
                return Error.Failure(
                    "Flights.ConfirmationFenceClosed",
                    "Synthetic supplier continuation closed."
                );
            return await ConfirmOrderAsync(providerOrderId, payment, expectedTotal, ct);
        }

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        )
        {
            Confirms++;
            if (cancellation is not null)
                throw cancellation;
            if (failure == "provider-throw")
                throw new HttpRequestException("private-supplier@example.test");
            Accepted = expectedTotal;
            return Task.FromResult<ErrorOr<ConfirmedOrder>>(
                failure == "provider-price"
                    ? Error.Conflict("Flights.OrderPriceChanged", "Price changed.")
                : failure == "provider-expired"
                    ? Error.Conflict("Flights.HoldExpired", "Hold expired.")
                : failure == "provider"
                    ? Error.Failure("Provider.Failure", "private-supplier@example.test")
                : new ConfirmedOrder(
                    providerOrderId,
                    Now,
                    SupplierPaymentEvidence
                        .Create("pay_fictional", expectedTotal, SupplierPaymentKind.Balance)
                        .Value
                )
            );
        }

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string reference,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            Travel.Modules.Flights.Core.Booking.BookingPurchase purchase,
            Guid attemptId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct)
        {
            Cancels++;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    private sealed class Wallet(string failure, OperationCanceledException? cancellation = null)
        : IPaymentGateway
    {
        public int Authorizations { get; private set; }
        public Money? Accepted { get; private set; }
        public int Refunds { get; private set; }

        public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
            Money amount,
            string key,
            CancellationToken ct
        )
        {
            Authorizations++;
            Accepted = amount;
            return Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());
        }

        public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct) =>
            cancellation is not null ? Task.FromException<ErrorOr<Success>>(cancellation)
            : failure == "capture-throw"
                ? Task.FromException<ErrorOr<Success>>(
                    new IOException("private-wallet@example.test")
                )
            : Task.FromResult<ErrorOr<Success>>(
                failure == "capture"
                    ? Error.Failure("Wallet.Capture", "private-wallet@example.test")
                    : Result.Success
            );

        public Task<ErrorOr<RefundRef>> RefundAsync(
            PaymentRef payment,
            Money amount,
            CancellationToken ct
        )
        {
            Refunds++;
            return Task.FromResult<ErrorOr<RefundRef>>(RefundRef.New());
        }
    }
}
