using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Webhooks;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class BookingCorrelationRecoveryTests(BookingReconcilerFixture fixture)
    : IClassFixture<BookingReconcilerFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirmed_stream_without_EF_recovers_ticket_or_direct_refund_after_reconcile(
        bool ticket
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = await fixture.SeedAsync(owner);
        var providerId = "ord-" + id;
        var payment = PaymentRef.New();
        await fixture.AppendAsync(
            id,
            new PaymentAuthorized(
                payment,
                BookingReconcilerFixture.Amount,
                BookingReconcilerFixture.Now
            ),
            new OrderConfirmed(providerId, payment, BookingReconcilerFixture.Now)
        );
        await using var db = new FlightsDbContext(fixture.Options);
        var entry = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = "duffel",
            EventId = Guid.NewGuid().ToString(),
            EventType = ticket ? "order.created" : "order.airline_initiated_change.cancelled",
            RawPayload = JsonSerializer.Serialize(
                new
                {
                    @object = new
                    {
                        id = providerId,
                        documents = new[] { new { type = "ticket", unique_identifier = "TKT" } },
                    },
                }
            ),
            ReceivedAt = BookingReconcilerFixture.Now,
            Signature = "test",
        };
        db.WebhookInbox.Add(entry);
        await db.SaveChangesAsync(ct);
        var inbox = new WebhookInboxStore(db, NullLogger<WebhookInboxStore>.Instance);
        var outbox = new RecordingMartenOutbox();
        async Task Deliver()
        {
            await using var session = fixture.Store.LightweightSession();
            await DuffelWebhookHandler.Handle(
                new ProcessDuffelWebhookCommand(entry.Id),
                inbox,
                session,
                NullFlightsMetricsImpl.Instance,
                outbox,
                TimeProvider.System,
                NullLogger<ProcessDuffelWebhookCommand>.Instance,
                ct
            );
        }
        await Should.ThrowAsync<BookingCorrelationNotReadyException>(Deliver);
        entry.ProcessedAt.ShouldBeNull();
        outbox.Published.ShouldBeEmpty();
        var reconciler = new OrderReadModelReconciler(
            fixture.Store,
            fixture.Options,
            new BookingProjectionMaintenanceContext()
        );
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
        await Deliver();
        entry.ProcessedAt.ShouldNotBeNull();
        outbox.Published.OfType<ReconcileOrderReadModel>().ShouldHaveSingleItem();
        if (ticket)
            outbox
                .Published.OfType<OrderTicketedNotification>()
                .ShouldHaveSingleItem()
                .RequiredStreamVersion.ShouldBe(5);
        else
            outbox.Published.OfType<OrderTicketedNotification>().ShouldBeEmpty();
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, ct);
        await using var verifyDb = new FlightsDbContext(fixture.Options);
        var row = await verifyDb.Orders.SingleAsync(x => x.AggregateId == id, ct);
        row.Status.ShouldBe(ticket ? "Ticketed" : "Refunded");
        row.ProjectedStreamVersion.ShouldBe(5);
        await using var verify = fixture.Store.QuerySession();
        var events = await verify.Events.FetchStreamAsync(id, token: ct);
        events.Count.ShouldBe(5);
        if (!ticket)
            events.ShouldNotContain(x => x.Data is OrderTicketed);
    }
}
