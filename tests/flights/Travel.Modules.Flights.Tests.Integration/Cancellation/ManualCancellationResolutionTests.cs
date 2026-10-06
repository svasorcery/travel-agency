using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class ManualCancellationResolutionTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task ManualEvidenceAndSourceLabels_legacy_inconclusive_audit_is_atomic_and_retry_precedes_stale_revision()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var now = TimeProvider.System.GetUtcNow();
        var store = fixture.Host.Services.GetRequiredService<IDocumentStore>();
        await using (var seed = store.LightweightSession())
        {
            seed.Events.StartStream<BookingAggregate>(
                id,
                CancellationTestFixture.HeldEvents(owner, now).Take(2).Cast<object>().ToArray()
            );
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var input = new ManualResolutionInput(
            id,
            ManualResolutionTargetKind.LegacyHeld,
            id,
            2,
            Guid.NewGuid(),
            ManualResolutionDecisionKind.RecordInconclusive,
            new("LEGACY-EVIDENCE", ManualEvidenceCategory.Inconclusive, now)
        );
        for (var replay = 0; replay < 2; replay++)
        {
            using var scope = fixture.Host.Services.CreateScope();
            var result = await ResolveCancellationReviewHandler.Handle(
                new(new OperatorActor(actor), input),
                scope.ServiceProvider.GetRequiredService<IDocumentSession>(),
                scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
                TimeProvider.System,
                TestContext.Current.CancellationToken
            );
            result.IsError.ShouldBeFalse();
            result.Value.RequestedOperationId.ShouldBeNull();
            result.Value.BlockingConfirmation!.Kind.ShouldBe("LegacyHeld");
        }
        await using var verify = store.LightweightSession();
        var events = await verify.Events.FetchStreamAsync(
            id,
            token: TestContext.Current.CancellationToken
        );
        events.Count.ShouldBe(3);
        var audit = events.Select(e => e.Data).OfType<BookingOperationReviewRecorded>().Single();
        audit.ActorId.ShouldBe(actor);
        audit.Source.ShouldBe(CancellationResolutionSource.OperatorVerified);
        var booking = (
            await verify.Events.FetchForWriting<BookingAggregate>(
                id,
                TestContext.Current.CancellationToken
            )
        ).Aggregate!;
        booking.MutationCoordinationEnabled.ShouldBeFalse();
        booking.CurrentConfirmationAttempt.ShouldBeNull();
        using var changedScope = fixture.Host.Services.CreateScope();
        var changed = await ResolveCancellationReviewHandler.Handle(
            new(
                new OperatorActor(actor),
                input with
                {
                    Evidence = input.Evidence with { EvidenceRef = "OTHER-EVIDENCE" },
                }
            ),
            changedScope.ServiceProvider.GetRequiredService<IDocumentSession>(),
            changedScope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            TimeProvider.System,
            TestContext.Current.CancellationToken
        );
        changed.IsError.ShouldBeTrue();
        changed.FirstError.Type.ShouldBe(ErrorOr.ErrorType.Conflict);
    }
}
