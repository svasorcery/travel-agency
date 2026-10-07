using System.Collections.Concurrent;
using ErrorOr;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

public sealed class ConvergenceExternalServices
    : IPaymentGateway,
        IFlightBookingProvider,
        IEmailSender,
        IUserDirectory
{
    public ConcurrentQueue<string> Authorizations { get; } = new();
    public ConcurrentQueue<string> Confirmations { get; } = new();
    public ConcurrentQueue<PaymentRef> Captures { get; } = new();
    public ConcurrentQueue<string> Emails { get; } = new();
    public ProviderId Id => ProviderId.Duffel;

    public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
        Money amount,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        Authorizations.Enqueue(idempotencyKey);
        return Task.FromResult<ErrorOr<PaymentRef>>(PaymentRef.New());
    }

    public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
    {
        Captures.Enqueue(payment);
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }

    public Task<ErrorOr<Success>> ValidateConfirmationAsync(
        string providerOrderId,
        Money expectedTotal,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

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
        Confirmations.Enqueue(providerOrderId);
        return Task.FromResult<ErrorOr<ConfirmedOrder>>(
            new ConfirmedOrder(
                providerOrderId,
                DateTimeOffset.UtcNow,
                SupplierPaymentEvidence
                    .Create("pay_fictional", expectedTotal, SupplierPaymentKind.Balance)
                    .Value
            )
        );
    }

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        Emails.Enqueue(toEmail);
        return Task.CompletedTask;
    }

    public Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<UserProfile?>(
            new UserProfile(userId, "test@example.test", "Test", "User", "en")
        );

    // Unexpected paths fail the test instead of silently returning success.
    public Task<ErrorOr<RefundRef>> RefundAsync(
        PaymentRef payment,
        Money amount,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
        string providerOfferRef,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
        BookableOffer offer,
        QuoteBinding binding,
        EquatableArray<BookingPassenger> passengers,
        Travel.Modules.Flights.Core.Booking.BookingPurchase purchase,
        Guid attemptId,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
        string providerOrderId,
        CancellationToken ct
    ) => throw new NotSupportedException();
}
