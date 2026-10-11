using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

// CI only. Reuses the existing persisted webhook fixture; no additional runtime harness.
public sealed partial class DuffelWebhookHandlerTests
{
    [Fact]
    public Task Signed_report_commits_without_money_then_refreshes_only_the_owned_ready_stream() =>
        VerifySignedOwnedStream(true);

    [Fact]
    public Task Signed_ticket_webhook_refreshes_the_owned_ticket_snapshot_after_projection() =>
        VerifySignedOwnedStream(false);

    private async Task VerifySignedOwnedStream(bool report)
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, providerId) = await SeedConfirmedStream();
        var options = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        var reconciler = new OrderReadModelReconciler(
            _store,
            options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);

        const string fixtureKey = "fictional-order-live-signing-key";
        var raw = report
            ? BuildAirlineCancelledPayload(providerId)
            : BuildOrderCreatedPayload(providerId, "DEMO-PIPELINE-TICKET");
        var bytes = Encoding.UTF8.GetBytes(raw);
        var signature =
            "t=1,v1="
            + Convert
                .ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(fixtureKey),
                        Encoding.UTF8.GetBytes("1." + raw)
                    )
                )
                .ToLowerInvariant();
        new DuffelWebhookVerifier(Options.Create(new DuffelOptions { WebhookSecret = fixtureKey }))
            .Verify(bytes, signature)
            .ShouldBeTrue();
        var inbox = CreateInboxRow(
            report ? "order.airline_initiated_change.cancelled" : "order.created",
            raw
        );
        using (var json = JsonDocument.Parse(raw))
            inbox.EventId = json.RootElement.GetProperty("id").GetString()!;
        inbox.Signature = signature;
        inbox.RawPayload = TestPii
            .Webhooks.Protect(inbox.Id, inbox.Source, inbox.EventId, inbox.EventType, bytes)
            .Value;
        await _db.SaveChangesAsync(ct);
        var outbox = new RecordingMartenOutbox();
        await using var session = _store.LightweightSession();
        await DuffelWebhookHandler.Handle(
            new ProcessDuffelWebhookCommand(inbox.Id),
            new WebhookInboxStore(_db, NullLogger<WebhookInboxStore>.Instance),
            session,
            NullMetrics,
            outbox,
            TimeProvider.System,
            NullLogger<ProcessDuffelWebhookCommand>.Instance,
            TestPii.WebhookReader,
            ct
        );
        var notification = report
            ? outbox.Published.OfType<OrderChangedNotification>().ShouldHaveSingleItem()
            : null;
        var ticket = report
            ? null
            : outbox.Published.OfType<OrderTicketedNotification>().ShouldHaveSingleItem();
        (notification?.RequiredStreamVersion ?? ticket!.RequiredStreamVersion).ShouldBe(4);
        var source = await session.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct);
        source!.Status.ShouldBe(report ? BookingStatus.Confirmed : BookingStatus.Ticketed);
        source.CustomerRefund.ShouldBeNull();
        if (report)
        {
            source.CurrentSupplierCancellationReport.ShouldNotBeNull();
            var money = CustomerRefundStatusFactory.Create(
                source,
                4,
                TimeProvider.System.GetUtcNow()
            );
            money.State.ShouldBe("ManualReviewRequired");
            money.Amount.ShouldBeNull();
            money.CustomerCredit.ShouldBe("NotEstablished");
        }
        inbox.ProcessedAt.ShouldNotBeNull();

        using var services = new ServiceCollection()
            .AddDbContext<FlightsDbContext>(o =>
                o.UseNpgsql(_pg.GetConnectionString()).UseSnakeCaseNamingConvention()
            )
            .BuildServiceProvider();
        var registry = new OrderSseConnectionRegistry(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderSseConnectionRegistry>.Instance
        );
        var readiness = new BookingNotificationReadiness(_store, options);
        Task Publish() =>
            report
                ? PublishOrderSseHandler.Handle(
                    notification!,
                    readiness,
                    registry,
                    TimeProvider.System,
                    ct
                )
                : PublishOrderSseHandler.Handle(
                    ticket!,
                    readiness,
                    registry,
                    TimeProvider.System,
                    ct
                );
        await Should.ThrowAsync<BookingReadModelNotReadyException>(() => Publish());
        var foreign = StreamContext(Guid.NewGuid());
        await OrderEventsSseEndpoint.Stream(id, foreign, registry, TimeProvider.System, ct);
        foreign.Response.StatusCode.ShouldBe(404);

        using var body = new MemoryStream();
        var owner = StreamContext(_userId);
        owner.Response.Body = body;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stream = OrderEventsSseEndpoint.Stream(
            id,
            owner,
            registry,
            TimeProvider.System,
            lifetime.Token
        );
        try
        {
            await WaitForText(body, ": connected", ct);
            await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
            await Publish();
            await WaitForText(body, "\"streamVersion\":4", ct);
            var wire = Encoding.UTF8.GetString(body.ToArray());
            wire.ShouldContain(report ? "event: OrderChanged" : "event: OrderTicketed");
            if (report)
                wire.ShouldContain("\"payload\":{}");
            await using var readDb = new FlightsDbContext(options);
            var view = await new OrderReadModelQueries(readDb).GetAsync(id, _userId, ct);
            OrderResponseMapper.From(view!).ProjectedStreamVersion.ShouldBe(4);
            if (!report)
                view!.TicketNumbers.ShouldContain("DEMO-PIPELINE-TICKET");
            (
                await new OrderReadModelQueries(readDb).GetAsync(id, Guid.NewGuid(), ct)
            ).ShouldBeNull();
        }
        finally
        {
            lifetime.Cancel();
            try
            {
                await stream;
            }
            catch (OperationCanceledException) { }
        }
    }

    private static DefaultHttpContext StreamContext(Guid owner) =>
        new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, owner.ToString())],
                    "fictional"
                )
            ),
        };

    private static async Task WaitForText(MemoryStream body, string text, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!Encoding.UTF8.GetString(body.ToArray()).Contains(text, StringComparison.Ordinal))
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Fictional stream did not produce expected frame.");
            await Task.Delay(10, ct);
        }
    }
}
