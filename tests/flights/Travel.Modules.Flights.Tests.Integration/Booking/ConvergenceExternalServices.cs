using System.Collections.Concurrent;
using ErrorOr;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

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

    public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        Confirmations.Enqueue(idempotencyKey);
        return Task.FromResult<ErrorOr<ConfirmedOrder>>(
            new ConfirmedOrder(providerOrderId, DateTimeOffset.UtcNow)
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

    public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
        BookableOffer offer,
        PassengerInfo passenger,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
        string providerOrderId,
        CancellationToken ct
    ) => throw new NotSupportedException();
}
