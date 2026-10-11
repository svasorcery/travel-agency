using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

[Trait("Category", "Integration")]
public sealed class BookingCreationDurabilityTests(AncillaryMartenFixture fixture)
    : IClassFixture<AncillaryMartenFixture>
{
    [Fact]
    public async Task Creation_read_version_remains_bound_to_its_snapshot_after_another_writer_commits()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await fixture.Quote();
        await using var reader = fixture.Store.LightweightSession();
        var original = await reader.Events.FetchForWriting<BookingAggregate>(id, ct);
        var owner = original.Aggregate!.OwnerUserId!.Value;
        var before = (
            await GetBookingCreationHandler.Handle(
                new GetBookingCreationQuery(id, owner),
                reader,
                TimeProvider.System,
                ct
            )
        ).Value;
        await using (var writer = fixture.Store.LightweightSession())
        {
            var stream = await writer.Events.FetchForWriting<BookingAggregate>(id, ct);
            stream.AppendOne(AncillaryMartenFixture.Start(stream.Aggregate!));
            await writer.SaveChangesAsync(ct);
        }
        before.BookingVersion.ShouldBe(2);
        before.Booking.CurrentCreation.ShouldBeNull();
        await using var fresh = fixture.Store.LightweightSession();
        var after = (
            await GetBookingCreationHandler.Handle(
                new GetBookingCreationQuery(id, owner),
                fresh,
                TimeProvider.System,
                ct
            )
        ).Value;
        after.BookingVersion.ShouldBe(3);
        after.Booking.CurrentCreation.ShouldNotBeNull();
    }

    [Fact]
    public async Task Two_writers_admit_only_one_retained_identity_and_restart_freezes_requote()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await fixture.Quote();
        await using var left = fixture.Store.LightweightSession();
        await using var right = fixture.Store.LightweightSession();
        var a = await left.Events.FetchForWriting<BookingAggregate>(id, ct);
        var b = await right.Events.FetchForWriting<BookingAggregate>(id, ct);
        var first = AncillaryMartenFixture.Start(a.Aggregate!);
        var second = AncillaryMartenFixture.Start(b.Aggregate!);
        a.AppendOne(first);
        b.AppendOne(second);
        await left.SaveChangesAsync(ct);
        await Should.ThrowAsync<Exception>(() => right.SaveChangesAsync(ct));
        await using var restarted = fixture.Store.QuerySession();
        var replay = (
            await restarted.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct)
        )!;
        replay.CurrentCreation!.Id.ShouldBe(first.AttemptId);
        replay.CreationAttempts.Count.ShouldBe(1);
        replay.DecideReQuote("off_fictional").ShouldBeOfType<BookingTransitionDecision.Rejected>();
        (await restarted.Events.FetchStreamAsync(id, token: ct))
            .Count(e => e.Data is BookingCreationStarted)
            .ShouldBe(1);
    }

    [Fact]
    public async Task Pre_start_requote_losing_compare_and_swap_cannot_replace_admitted_binding()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await fixture.Quote();
        await using var refresh = fixture.Store.LightweightSession();
        await using var admission = fixture.Store.LightweightSession();
        var stale = await refresh.Events.FetchForWriting<BookingAggregate>(id, ct);
        var current = await admission.Events.FetchForWriting<BookingAggregate>(id, ct);
        var original = current.Aggregate!.QuoteBinding!;
        var started = AncillaryMartenFixture.Start(current.Aggregate);
        current.AppendOne(started);
        await admission.SaveChangesAsync(ct);
        var replacement = Travel
            .Modules.Flights.Core.ValueObjects.QuoteBinding.Create(
                Guid.NewGuid(),
                original.Party,
                original.Slots
            )
            .Value;
        stale.AppendOne(
            new OfferReQuoted(
                stale.Aggregate!.OfferId!.Value,
                stale.Aggregate.TotalAmount!,
                stale.Aggregate.TotalAmount!,
                AncillaryMartenFixture.Now,
                QuoteBinding: replacement
            )
        );
        await Should.ThrowAsync<Exception>(() => refresh.SaveChangesAsync(ct));
        await using var verify = fixture.Store.QuerySession();
        var retained = (await verify.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct))!;
        retained.QuoteBinding!.Revision.ShouldBe(original.Revision);
        retained.CurrentCreation!.QuoteRevision.ShouldBe(original.Revision);
    }

    [Fact]
    public async Task Abandoned_transaction_leaves_no_saved_start_or_false_creation_proof()
    {
        var id = await fixture.Quote();
        var ct = TestContext.Current.CancellationToken;
        await using (var abandoned = fixture.Store.LightweightSession())
        {
            var stream = await abandoned.Events.FetchForWriting<BookingAggregate>(id, ct);
            stream.AppendOne(AncillaryMartenFixture.Start(stream.Aggregate!));
        }
        await using var restarted = fixture.Store.QuerySession();
        var booking = (
            await restarted.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct)
        )!;
        booking.CurrentCreation.ShouldBeNull();
        booking.Purchase!.Total.Amount.ShouldBe(70);
        (await restarted.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(2);
    }
}
