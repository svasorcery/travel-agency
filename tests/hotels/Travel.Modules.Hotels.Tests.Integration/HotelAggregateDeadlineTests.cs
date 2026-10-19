using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelAggregateDeadlineTests
{
    [Fact]
    public async Task Already_completed_source_cannot_publish_after_aggregate_deadline()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var cache = new HotelSnapshotCache(clock);
        var source = new Source(_ =>
        {
            clock.Advance(TimeSpan.FromSeconds(18));
            return Task.FromResult(new HotelExecution<HotelOffer>(ExecutionKind.Complete, []));
        });
        var service = new HotelSearchService([source], cache, clock);
        var result = await service.SearchAsync(Request(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        cache.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Child_timeout_freezes_failure_and_late_success_cannot_mutate_snapshot()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var reply = new TaskCompletionSource<HotelExecution<HotelOffer>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var cache = new HotelSnapshotCache(clock);
        var service = new HotelSearchService([new Source(_ => reply.Task)], cache, clock);
        var pending = service.SearchAsync(Request(), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(15));
        var result = await pending.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Failed);
        reply.SetResult(new(ExecutionKind.Complete, []));
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Failed);
        result.Value.Statuses.Single().Reason.ShouldBe("Timeout");
    }

    private static StaySearch Request() =>
        new(HotelSearchService.Areas[3], new(new(2027, 1, 3), new(2027, 1, 5)), new(1));

    private sealed class Source(Func<CancellationToken, Task<HotelExecution<HotelOffer>>> execute)
        : IHotelSearchProvider
    {
        public string Key => "owned";

        public CapabilitySupport GetSupport(StaySearch request) => CapabilitySupport.Yes;

        public Task<HotelExecution<HotelOffer>> SearchAsync(
            StaySearch request,
            CancellationToken ct
        ) => execute(ct);

        public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
            SelectedStay selection,
            CancellationToken ct
        ) => execute(ct);
    }
}
