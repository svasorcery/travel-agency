using ErrorOr;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class CancellationRefreshConcurrencyTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task OwnerOperatorShareRefreshLimit_and_duplicate_identity_does_not_enqueue_again()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var admission = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Cast<object>().ToArray()
            );
            seed.Events.Append(
                id,
                new CancellationPreparationStarted(
                    operation,
                    owner,
                    "ord_fictional",
                    new string('a', 64),
                    new string('b', 64),
                    admission,
                    now
                ),
                new CancellationPreparationDispatched(
                    operation,
                    admission,
                    Guid.NewGuid(),
                    RecoverySchedule.Create(Guid.NewGuid(), now).Value,
                    now
                ),
                new CancellationOutcomeBecameUnknown(
                    operation,
                    CancellationUnknownStage.Preparation,
                    CancellationReason.Pending,
                    now
                )
            );
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        long revision;
        await using (var read = store.LightweightSession())
            revision = (
                await read.Events.FetchForWriting<BookingAggregate>(
                    id,
                    TestContext.Current.CancellationToken
                )
            )
                .Aggregate!
                .CurrentCancellation!
                .Revision;
        var commands = new[]
        {
            new RefreshCancellationCommand(id, owner, operation, revision, Guid.NewGuid(), false),
            new RefreshCancellationCommand(
                id,
                Guid.NewGuid(),
                operation,
                revision,
                Guid.NewGuid(),
                true
            ),
        };
        async Task<ErrorOr<CancellationStatusResult>> Run(RefreshCancellationCommand command)
        {
            using var scope = fixture.Host.Services.CreateScope();
            return await RefreshCancellationHandler.Handle(
                command,
                scope.ServiceProvider.GetRequiredService<IDocumentSession>(),
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                TimeProvider.System,
                TestContext.Current.CancellationToken
            );
        }
        var results = await Task.WhenAll(commands.Select(Run));
        results.Count(result => !result.IsError).ShouldBe(1);
        var loser = results.Single(result => result.IsError).FirstError;
        (loser.Type == ErrorType.Conflict || loser.NumericType == 429).ShouldBeTrue();
        var winner = commands[Array.FindIndex(results, result => !result.IsError)];
        (await Run(winner)).IsError.ShouldBeFalse();
        await using var verify = store.LightweightSession();
        var events = await verify.Events.FetchStreamAsync(
            id,
            token: TestContext.Current.CancellationToken
        );
        events.Count(e => e.Data is CancellationRefreshRequested).ShouldBe(1);
        var booking = (
            await verify.Events.FetchForWriting<BookingAggregate>(
                id,
                TestContext.Current.CancellationToken
            )
        ).Aggregate!;
        booking.NextOwnerRefreshAt.ShouldNotBeNull();
        booking.NextOwnerRefreshAt.Value.ShouldBeGreaterThan(now);
        var limited = await Run(
            new(
                id,
                Guid.NewGuid(),
                operation,
                booking.CurrentCancellation!.Revision,
                Guid.NewGuid(),
                true
            )
        );
        limited.IsError.ShouldBeTrue();
        limited.FirstError.NumericType.ShouldBe(429);
        limited.FirstError.Metadata!["retryAfter"].ShouldBeOfType<int>().ShouldBeGreaterThan(0);
    }
}
