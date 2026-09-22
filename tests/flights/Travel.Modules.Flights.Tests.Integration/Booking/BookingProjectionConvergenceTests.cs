using System.Threading.Channels;
using ErrorOr;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class BookingProjectionConvergenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private readonly ConvergenceFaults _faults = new();
    private readonly ConvergenceExternalServices _external = new();
    private IHost _host = default!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private IDocumentStore Store => _host.Services.GetRequiredService<IDocumentStore>();
    private IWolverineRuntime Runtime => _host.Services.GetRequiredService<IWolverineRuntime>();

    public async ValueTask InitializeAsync() => await _pg.StartAsync(Ct);

    public async ValueTask DisposeAsync()
    {
        _faults.ReleaseAll();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _pg.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_confirm_recovers_projection_and_version_gated_notifications(
        bool controlledRestart
    )
    {
        await StartAsync(recoveryEnabled: !controlledRestart);
        var owner = Guid.NewGuid();
        var id = await SeedHeldAsync(owner);
        var before = await ReadOrderAsync(id);
        before.ProjectedStreamVersion.ShouldBe(2);
        _faults.AggregateId = id;
        _faults.Window = ConvergenceFaultWindow.BeforeProjectionSave;
        _faults.Armed = true;
        var channel = Register(id);

        // The only user command in either host. Recovery must come from its original envelope.
        await ConfirmAsync(id, owner);
        await ConvergenceFaults.WaitAsync(_faults.AtFault.Task, Ct);
        await AssertConfirmedStreamAsync(id);
        var envelopeId = _faults.ReconcileEnvelopes.ShouldHaveSingleItem();
        (await Runtime.Storage.Admin.AllIncomingAsync()).ShouldContain(x => x.Id == envelopeId);
        // An independent DB read sees the committed events/envelope BEFORE we release the fault.
        (await ReadOrderAsync(id)).ProjectedStreamVersion.ShouldBe(2);
        var notificationId = await PendingNotificationAfterFailureAsync();
        _external.Emails.ShouldBeEmpty();
        channel.Reader.TryPeek(out _).ShouldBeFalse();
        AssertExternalCalls(id);
        _faults.ReleaseFault.TrySetResult();

        if (controlledRestart)
        {
            await WaitAsync(async () =>
                (await Runtime.Storage.Admin.AllIncomingAsync()).Any(x =>
                    x.Id == envelopeId && x.Status == EnvelopeStatus.Scheduled
                )
            );
            _faults.ReconcileEnvelopes.Count.ShouldBe(1);
            _host.Services.GetRequiredService<IOrderSseRegistry>().Unregister(id, channel);
            await _host.StopAsync(Ct);
            // The disabled recovery agent in A cannot silently finish the scheduled work during shutdown.
            (await ReadOrderAsync(id)).ProjectedStreamVersion.ShouldBe(2);
            // Host A's storage admin carries its cancelled lifecycle token after StopAsync.
            // Verify persistence through an independent connection while neither host runs.
            await using (var connection = new NpgsqlConnection(_pg.GetConnectionString()))
            {
                await connection.OpenAsync(Ct);
                await using var command = new NpgsqlCommand(
                    "SELECT count(*) FROM public.wolverine_incoming_envelopes WHERE id = ANY(@ids)",
                    connection
                );
                command.Parameters.AddWithValue("ids", new[] { envelopeId, notificationId });
                (await command.ExecuteScalarAsync(Ct)).ShouldBe(2L);
            }
            _host.Dispose();
            await StartAsync(recoveryEnabled: true);
            channel = Register(id); // A new connection; no cross-connection replay claim.
        }
        await ConvergenceFaults.WaitAsync(_faults.AtRetry.Task, Ct);
        (await ReadOrderAsync(id)).ProjectedStreamVersion.ShouldBe(2);
        _faults.ReconcileEnvelopes.ToArray().ShouldBe(new[] { envelopeId, envelopeId });
        _faults.ReleaseRetry.TrySetResult();
        await WaitAsync(async () => (await ReadOrderAsync(id)).ProjectedStreamVersion == 4);
        await WaitAsync(() => Task.FromResult(_external.Emails.Count == 1));
        var evt = await channel
            .Reader.ReadAsync(Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), Ct);
        evt.Type.ShouldBe("OrderConfirmed");
        evt.StreamVersion.ShouldBe(4);
        await AssertDrainedAsync(envelopeId, notificationId);
        _faults.Notifications.Select(x => x.Id).Distinct().ShouldBe(new[] { notificationId });
        _faults.Notifications.Count.ShouldBeGreaterThanOrEqualTo(2);
        _faults.Notifications.ShouldAllBe(x => x.Version == 4);
        var after = await ReadOrderAsync(id);
        after.Id.ShouldBe(before.Id);
        after.Status.ShouldBe("Confirmed");
        after.UserId.ShouldBe(owner);
        _faults.ProjectionContexts.Count.ShouldBe(2);
        _faults.ProjectionContexts.Distinct().Count().ShouldBe(2);
        _faults.InjectedFaults.ShouldBe(1);
        await AssertConfirmedStreamAsync(id);
        AssertExternalCalls(id);
        _host.Services.GetRequiredService<IOrderSseRegistry>().Unregister(id, channel);
    }

    [Fact]
    public async Task Failure_after_EF_commit_redelivers_same_envelope_without_another_save_or_regression()
    {
        await StartAsync(true);
        var owner = Guid.NewGuid();
        var id = await SeedHeldAsync(owner);
        _faults.AggregateId = id;
        _faults.Window = ConvergenceFaultWindow.AfterProjectionCommit;
        _faults.Armed = true;
        await ConfirmAsync(id, owner);
        await ConvergenceFaults.WaitAsync(_faults.AtFault.Task, Ct);
        var committed = await ReadOrderAsync(id);
        committed.ProjectedStreamVersion.ShouldBe(4);
        committed.Status.ShouldBe("Confirmed");
        await AssertConfirmedStreamAsync(id);
        var envelopeId = _faults.ReconcileEnvelopes.ShouldHaveSingleItem();
        (await Runtime.Storage.Admin.AllIncomingAsync()).ShouldContain(x => x.Id == envelopeId);
        _faults.ReleaseFault.TrySetResult();
        await ConvergenceFaults.WaitAsync(_faults.AtRetry.Task, Ct);
        _faults.ReconcileEnvelopes.ToArray().ShouldBe(new[] { envelopeId, envelopeId });
        _faults.ReleaseRetry.TrySetResult();
        await AssertDrainedAsync(envelopeId);
        var after = await ReadOrderAsync(id);
        after.ShouldBeEquivalentTo(committed);
        _faults.ProjectionContexts.ShouldHaveSingleItem(); // retry read the checkpoint and did not save
        _faults.InjectedFaults.ShouldBe(1);
        await AssertConfirmedStreamAsync(id);
        AssertExternalCalls(id);
    }

    private async Task ConfirmAsync(Guid id, Guid owner)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeAsync<ErrorOr<ConfirmedOrderResult>>(new ConfirmOrderCommand(id, owner), Ct)
        ).IsError.ShouldBeFalse();
    }

    private async Task StartAsync(bool recoveryEnabled) =>
        _host = await WolverineOutboxFixture.StartConvergenceHostAsync(
            _pg.GetConnectionString(),
            _faults,
            _external,
            recoveryEnabled,
            Ct
        );

    private Channel<SseEvent> Register(Guid id)
    {
        var channel = Channel.CreateUnbounded<SseEvent>();
        _host.Services.GetRequiredService<IOrderSseRegistry>().Register(id, channel);
        return channel;
    }

    private async Task<Guid> SeedHeldAsync(Guid owner)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        await using (var session = Store.LightweightSession())
        {
            session.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    OfferId.New(),
                    Itinerary.Create([Slice.Create([segment]).Value]).Value,
                    BookingReconcilerFixture.Amount,
                    now.AddHours(1),
                    "off-test",
                    now
                ),
                BookingReconcilerFixture.Held(owner) with
                {
                    OrderId = "ord_" + id,
                    HeldUntil = now.AddHours(1),
                    HeldAt = now,
                }
            );
            await session.SaveChangesAsync(Ct);
        }
        // Fixture setup only: establish a real Held read model before arming fault injection.
        await using var scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
            .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        return id;
    }

    private async Task<OrderReadModelEntity> ReadOrderAsync(Guid id)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await EntityFrameworkQueryableExtensions.SingleAsync(
            scope.ServiceProvider.GetRequiredService<FlightsDbContext>().Orders.AsNoTracking(),
            x => x.AggregateId == id,
            Ct
        );
    }

    private async Task AssertConfirmedStreamAsync(Guid id)
    {
        await using var verify = Store.QuerySession();
        var events = await verify.Events.FetchStreamAsync(id, token: Ct);
        events.Count.ShouldBe(4);
        events.Count(x => x.Data is PaymentAuthorized).ShouldBe(1);
        events.Count(x => x.Data is OrderConfirmed).ShouldBe(1);
    }

    private void AssertExternalCalls(Guid id)
    {
        _external.Authorizations.ToArray().ShouldBe(new[] { id.ToString("N") });
        _external.Confirmations.ToArray().ShouldBe(new[] { id.ToString("N") });
        _external.Captures.ShouldHaveSingleItem();
    }

    private async Task<Guid> PendingNotificationAfterFailureAsync()
    {
        Guid result = default;
        await WaitAsync(async () =>
        {
            var envelope = (await Runtime.Storage.Admin.AllIncomingAsync()).SingleOrDefault(x =>
                x.MessageType == typeof(OrderConfirmedNotification).FullName
                && _faults.Notifications.Any(n => n.Id == x.Id)
                // If the scheduler already claimed the retry, middleware holds it.
                // Do not race the 2s schedule when observing the pending envelope.
                && (
                    x.Status == EnvelopeStatus.Scheduled
                    || _faults.Notifications.Count(n => n.Id == x.Id) >= 2
                )
            );
            if (envelope is null)
                return false;
            result = envelope.Id;
            return true;
        });
        return result;
    }

    private async Task AssertDrainedAsync(params Guid[] ids)
    {
        await WaitAsync(async () =>
            !(await Runtime.Storage.Admin.AllIncomingAsync()).Any(x =>
                ids.Contains(x.Id) && x.Status != EnvelopeStatus.Handled
            )
        );
        (await Runtime.Storage.Admin.AllOutgoingAsync()).ShouldNotContain(x => ids.Contains(x.Id));
        foreach (var id in ids)
            (await Runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(id)).ShouldBeNull();
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        while (!await condition())
            await Task.Delay(50, timeout.Token);
    }
}
