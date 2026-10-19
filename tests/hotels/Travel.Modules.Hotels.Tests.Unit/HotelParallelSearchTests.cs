using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelParallelSearchTests
{
    [Fact]
    public async Task Both_sources_start_before_either_finishes_and_zero_partial_is_limited()
    {
        var a = new ControlledSource("a");
        var b = new ControlledSource("b");
        var cache = new HotelSnapshotCache(TimeProvider.System);
        var service = new HotelSearchService([a, b], cache, TimeProvider.System);
        var pending = service.SearchAsync(Request(), TestContext.Current.CancellationToken);
        await a.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );
        var bothStarted = b.Started.Task.IsCompleted;
        a.Reply.TrySetResult(new(ExecutionKind.Complete, []));
        b.Reply.TrySetResult(new(ExecutionKind.Failed, [], "Timeout"));
        var result = await pending;
        bothStarted.ShouldBeTrue();
        result.Value.Result.Kind.ShouldBe(ExecutionKind.Limited);
        result.Value.Statuses.Length.ShouldBe(2);
        result.Value.Statuses[0].Reason.ShouldBe("NoInventory");
        result.Value.Statuses[0].ResultCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelled_uncooperative_children_retain_module_permits_until_completion()
    {
        var source = new ControlledSource("a");
        var cache = new HotelSnapshotCache(TimeProvider.System);
        var service = new HotelSearchService([source], cache, TimeProvider.System);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var first = service.SearchAsync(Request(), cancel.Token);
        var second = service.SearchAsync(Request(), cancel.Token);
        await source.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );
        cancel.Cancel();
        var completed = await Task.WhenAny(
            Task.WhenAll(first, second),
            Task.Delay(1000, TestContext.Current.CancellationToken)
        );
        var promptly = completed != null && first.IsCompleted && second.IsCompleted;
        // Complete underlying work even on the RED path so this test never leaks a child.
        var third = service.SearchAsync(Request(), TestContext.Current.CancellationToken);
        source.Reply.TrySetResult(new(ExecutionKind.Complete, []));
        try
        {
            await first;
        }
        catch (OperationCanceledException) { }
        try
        {
            await second;
        }
        catch (OperationCanceledException) { }
        var refused = await third;
        promptly.ShouldBeTrue();
        refused.IsError.ShouldBeTrue();
        refused.FirstError.ShouldBe(HotelErrors.Capacity);
        cache.Count.ShouldBe(0);
    }

    private static StaySearch Request() =>
        new(
            HotelSearchService.Areas[3],
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)
            ),
            new(1)
        );

    private sealed class ControlledSource(string key) : IHotelSearchProvider
    {
        public string Key => key;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HotelExecution<HotelOffer>> Reply { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CapabilitySupport GetSupport(StaySearch request) => CapabilitySupport.Yes;

        public Task<HotelExecution<HotelOffer>> SearchAsync(
            StaySearch request,
            CancellationToken ct
        )
        {
            Started.TrySetResult();
            return Reply.Task;
        }

        public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
            SelectedStay selection,
            CancellationToken ct
        ) => SearchAsync(selection.Request, ct);
    }
}
