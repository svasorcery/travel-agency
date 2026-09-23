using System.Threading.Channels;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.ReadModels;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

public sealed class NotificationVersionGateTests
{
    [Fact]
    public void All_legacy_envelope_types_keep_missing_version_distinct_from_explicit_zero()
    {
        var json =
            """{"AggregateId":"11111111-1111-1111-1111-111111111111","UserId":"22222222-2222-2222-2222-222222222222"}""";
        System
            .Text.Json.JsonSerializer.Deserialize<OrderConfirmedNotification>(json)!
            .RequiredStreamVersion.ShouldBeNull();
        System
            .Text.Json.JsonSerializer.Deserialize<OrderTicketedNotification>(json)!
            .RequiredStreamVersion.ShouldBeNull();
        var cancelled = System.Text.Json.JsonSerializer.Deserialize<OrderCancelledNotification>(
            json
        )!;
        cancelled.RequiredStreamVersion.ShouldBeNull();
        cancelled.Reason.ShouldBe(Travel.Modules.Flights.Core.DomainEvents.CancelReason.User);
        var explicitZero = json[..^1] + ",\"RequiredStreamVersion\":0}";
        System
            .Text.Json.JsonSerializer.Deserialize<OrderConfirmedNotification>(explicitZero)!
            .RequiredStreamVersion.ShouldBe(0);
    }

    [Theory]
    [InlineData("confirm", "Confirmed", "OrderConfirmed")]
    [InlineData("confirm", "Ticketed", "OrderTicketed")]
    [InlineData("confirm", "Cancelled", null)]
    [InlineData("confirm", "Refunded", null)]
    [InlineData("ticket", "Ticketed", "OrderTicketed")]
    [InlineData("ticket", "Confirmed", null)]
    [InlineData("ticket", "Cancelled", null)]
    [InlineData("ticket", "Refunded", null)]
    [InlineData("cancel", "Cancelled", "OrderCancelled")]
    [InlineData("cancel", "Refunded", null)]
    public async Task SSE_uses_ready_current_state_and_version(
        string kind,
        string status,
        string? expected
    )
    {
        var readiness = new Ready(status);
        var registry = new RecordingRegistry();
        await Publish(kind, readiness, registry);
        registry.Events.Count.ShouldBe(expected is null ? 0 : 1);
        if (expected is not null)
        {
            registry.Events[0].Type.ShouldBe(expected);
            registry.Events[0].StreamVersion.ShouldBe(6);
        }
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("ticket")]
    [InlineData("cancel")]
    public async Task Not_ready_never_publishes_SSE(string kind)
    {
        var registry = new RecordingRegistry();
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() =>
            Publish(kind, new Ready(null), registry)
        );
        registry.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "Confirmed", 1)]
    [InlineData(true, "Ticketed", 1)]
    [InlineData(true, "Cancelled", 0)]
    [InlineData(true, "Refunded", 0)]
    [InlineData(false, "Cancelled", 1)]
    [InlineData(false, "Confirmed", 0)]
    [InlineData(false, "Refunded", 0)]
    [InlineData(true, null, 0)]
    [InlineData(false, null, 0)]
    public async Task Emails_wait_for_projection_and_suppress_obsolete_states(
        bool confirmation,
        string? status,
        int count
    )
    {
        var effects = new EmailEffects();
        var ready = new Ready(status);
        Task Send() =>
            confirmation
                ? SendOrderConfirmationEmailHandler.Handle(
                    new OrderConfirmedNotification(Guid.NewGuid(), Guid.NewGuid(), 4),
                    ready,
                    effects,
                    effects,
                    effects,
                    TestContext.Current.CancellationToken
                )
                : SendOrderCancellationEmailHandler.Handle(
                    new OrderCancelledNotification(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        RequiredStreamVersion: 4
                    ),
                    ready,
                    effects,
                    effects,
                    effects,
                    TestContext.Current.CancellationToken
                );
        if (status is null)
            await Should.ThrowAsync<BookingReadModelNotReadyException>(Send);
        else
            await Send();
        effects.Sent.ShouldBe(count);
        effects.Rendered.ShouldBe(count);
        effects.UserLookups.ShouldBe(count);
    }

    private sealed class EmailEffects : IEmailSender, IEmailRenderer, IUserDirectory
    {
        public int Sent { get; private set; }
        public int Rendered { get; private set; }
        public int UserLookups { get; private set; }

        public Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string textBody,
            CancellationToken ct
        )
        {
            Sent++;
            return Task.CompletedTask;
        }

        public Task<RenderedEmail> RenderAsync(
            string templateName,
            OrderEmailModel model,
            System.Globalization.CultureInfo locale,
            CancellationToken ct
        )
        {
            Rendered++;
            return Task.FromResult(new RenderedEmail("subject", "html", "text"));
        }

        public Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct)
        {
            UserLookups++;
            return Task.FromResult<UserProfile?>(
                new UserProfile(userId, "user@example.test", "Test", "User", "en")
            );
        }
    }

    private static Task Publish(
        string kind,
        IBookingNotificationReadiness readiness,
        IOrderSseRegistry registry
    ) =>
        kind switch
        {
            "confirm" => PublishOrderSseHandler.Handle(
                new OrderConfirmedNotification(Guid.NewGuid(), Guid.NewGuid(), 4),
                readiness,
                registry,
                TimeProvider.System,
                TestContext.Current.CancellationToken
            ),
            "ticket" => PublishOrderSseHandler.Handle(
                new OrderTicketedNotification(Guid.NewGuid(), Guid.NewGuid(), 4),
                readiness,
                registry,
                TimeProvider.System,
                TestContext.Current.CancellationToken
            ),
            _ => PublishOrderSseHandler.Handle(
                new OrderCancelledNotification(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    RequiredStreamVersion: 4
                ),
                readiness,
                registry,
                TimeProvider.System,
                TestContext.Current.CancellationToken
            ),
        };

    private sealed class Ready(string? status) : IBookingNotificationReadiness
    {
        public Task<OrderView> RequireAsync(Guid id, Guid user, long? version, CancellationToken ct)
        {
            version.ShouldBe(4);
            if (status is null)
                throw new BookingReadModelNotReadyException();
            return Task.FromResult(
                new OrderView(
                    id,
                    user,
                    "ord",
                    status,
                    100,
                    "USD",
                    "{}",
                    "{}",
                    [],
                    DateTimeOffset.UnixEpoch,
                    null,
                    null,
                    null,
                    6
                )
            );
        }
    }

    private sealed class RecordingRegistry : IOrderSseRegistry
    {
        public List<SseEvent> Events { get; } = [];

        public void Register(Guid orderId, Channel<SseEvent> channel) { }

        public void Unregister(Guid orderId, Channel<SseEvent> channel) { }

        public void Publish(Guid orderId, SseEvent evt) => Events.Add(evt);

        public Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes) { }
    }
}
