using System.Text.Json;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Shared.Abstractions;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class CancellationCommitTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task Successful_commit_persists_event_and_all_scheduled_work_together()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using var scope = fixture.Host.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var outbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
        session.Events.StartStream<BookingAggregate>(
            id,
            CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
        );
        var work = Enumerable
            .Range(0, 5)
            .Select(_ => new BookingWork(
                new CancellationStorageProbe(id, Guid.NewGuid()),
                now.AddHours(1)
            ))
            .ToArray();
        await session.SaveBookingWithWorkAsync(outbox, id, work, [], ct);
        using var verify = fixture.Host.Services.CreateScope();
        var reader = verify.ServiceProvider.GetRequiredService<IDocumentSession>();
        var loaded = await reader.Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.OwnerUserId.ShouldBe(owner);
        loaded.Aggregate.MutationCoordinationEnabled.ShouldBeTrue();
        (await PendingWork(id)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task Losing_prepare_commit_cannot_leave_its_work_or_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        using (var seed = fixture.Host.Services.CreateScope())
        {
            var session = seed.ServiceProvider.GetRequiredService<IDocumentSession>();
            session.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
            );
            await session.SaveChangesAsync(ct);
        }
        using var winner = fixture.Host.Services.CreateScope();
        using var loser = fixture.Host.Services.CreateScope();
        var first = winner.ServiceProvider.GetRequiredService<IDocumentSession>();
        var second = loser.ServiceProvider.GetRequiredService<IDocumentSession>();
        var a = await first.Events.FetchForWriting<BookingAggregate>(id, ct);
        var b = await second.Events.FetchForWriting<BookingAggregate>(id, ct);
        var firstOperation = Guid.NewGuid();
        var secondOperation = Guid.NewGuid();
        var admitted = a.Aggregate!.DecideCancellationPrepare(
            owner,
            firstOperation,
            a.CurrentVersion!.Value,
            new string('a', 64),
            Guid.NewGuid(),
            now,
            new string('b', 64)
        );
        var competing = b.Aggregate!.DecideCancellationPrepare(
            owner,
            secondOperation,
            b.CurrentVersion!.Value,
            new string('c', 64),
            Guid.NewGuid(),
            now,
            new string('b', 64)
        );
        foreach (var e in admitted.Events)
            a.AppendOne(e);
        foreach (var e in competing.Events)
            b.AppendOne(e);
        var kept = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        await first.SaveBookingWithWorkAsync(
            winner.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            id,
            [new(new CancellationStorageProbe(id, kept), now.AddHours(1))],
            [],
            ct
        );
        await Should.ThrowAsync<BookingWriteConflictException>(() =>
            second.SaveBookingWithWorkAsync(
                loser.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                id,
                [new(new CancellationStorageProbe(id, rejected), now.AddHours(1))],
                [],
                ct
            )
        );
        var pending = await PendingWork(id);
        pending.ShouldContain(kept);
        pending.ShouldNotContain(rejected);
        using var verify = fixture.Host.Services.CreateScope();
        var loaded = await verify
            .ServiceProvider.GetRequiredService<IDocumentSession>()
            .Events.FetchForWriting<BookingAggregate>(id, ct);
        loaded.Aggregate!.CancellationOperations.Keys.ShouldBe([firstOperation]);
    }

    private async Task<HashSet<Guid>> PendingWork(Guid aggregateId)
    {
        var runtime = fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var envelopes = (await runtime.Storage.Admin.AllIncomingAsync()).Concat(
            await runtime.Storage.Admin.AllOutgoingAsync()
        );
        var result = new HashSet<Guid>();
        foreach (
            var e in envelopes.Where(e =>
                e.MessageType == typeof(CancellationStorageProbe).FullName && e.Data is not null
            )
        )
        {
            var payload = JsonSerializer.Deserialize<CancellationStorageProbe>(
                e.Data!,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            );
            if (payload?.AggregateId == aggregateId)
                result.Add(payload.WorkId);
        }
        return result;
    }
}
