using Marten;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

[Trait("Category", "Integration")]
public sealed class AncillaryProjectionTests(AncillaryMartenFixture fixture)
    : IClassFixture<AncillaryMartenFixture>
{
    [Fact]
    public async Task Persisted_quote_and_admission_replay_advance_checkpoint_without_partial_row()
    {
        var id = await fixture.Quote();
        var ct = TestContext.Current.CancellationToken;
        await using (var writer = fixture.Store.LightweightSession())
        {
            var stream = await writer.Events.FetchForWriting<BookingAggregate>(id, ct);
            stream.AppendOne(AncillaryMartenFixture.Start(stream.Aggregate!));
            await writer.SaveChangesAsync(ct);
        }
        await using var reader = fixture.Store.QuerySession();
        var events = await reader.Events.FetchStreamAsync(id, token: ct);
        var row = new OrderReadModelEntity { AggregateId = id, ProjectedStreamVersion = 0 };
        foreach (var e in events)
            OrderReadModelEventApplier.Apply(row, e);
        row.ProjectedStreamVersion.ShouldBe(3);
        row.TotalAmount.ShouldBe(70);
        row.UserId.ShouldNotBeNull();
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeFalse();
        row.PassengerInfoJson.ShouldBeNullOrEmpty();
    }
}
