using System.Collections.Immutable;
using ErrorOr;
using Shouldly;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Xunit;

namespace Travel.Modules.Rail.Tests.Unit;

public sealed class ReferenceServiceTests
{
    internal sealed class Profile : IRailRuntimeProfile
    {
        public RailProfile Current { get; set; } = new("epoch", "fictional", true, default);
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Loader : IRailReferenceLoader
    {
        public int Calls;
        public TaskCompletionSource<ErrorOr<ReferenceData>> Completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<ErrorOr<ReferenceData>> LoadAsync(
            RailProfile profile,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref Calls);
            return Completion.Task;
        }
    }

    internal static ReferenceData Data(DateTimeOffset now) =>
        new(
            new CatalogueData(
                Enumerable
                    .Range(1, 25)
                    .Select(x => new RailStation(
                        StationRef.Create("yandex-rasp", "s" + x).Value,
                        "Станция " + x,
                        "Город",
                        "Регион"
                    ))
                    .ToImmutableArray(),
                false,
                1000
            ),
            new RailAttribution(
                "/rail-fictional-banner.html",
                "Вымышленные данные",
                "https://rasp.yandex.ru/"
            ),
            now
        );

    [Fact]
    public async Task Cold_waiters_are_bounded_and_share_atomic_bundle()
    {
        var loader = new Loader();
        var clock = new Clock();
        var service = new RailReferenceService(new Profile(), loader, clock);
        var a = service.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        var b = service.SuggestAsync("Город", TestContext.Current.CancellationToken);
        var overflow = await service.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        overflow.IsError.ShouldBeTrue();
        overflow.FirstError.Code.ShouldBe("Rail.Capacity");
        loader.Completion.SetResult(Data(clock.Now));
        var results = await Task.WhenAll(a, b);
        loader.Calls.ShouldBe(1);
        results[0].Value.Suggestions.Length.ShouldBe(20);
        results[0].Value.Kind.ShouldBe("limited");
        results[0].Value.CatalogueGeneration.ShouldBe(results[1].Value.CatalogueGeneration);
    }

    [Fact]
    public async Task Cancelling_all_waiters_quarantines_uncooperative_loader_and_no_late_publish()
    {
        var loader = new Loader();
        var clock = new Clock();
        var service = new RailReferenceService(new Profile(), loader, clock);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var pending = service.SuggestAsync("Стан", cancellation.Token);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        var blocked = await service.SuggestAsync("Стан", TestContext.Current.CancellationToken);
        blocked.Value.Reasons.ShouldContain("loading-cancelled");
        loader.Calls.ShouldBe(1);
        loader.Completion.SetResult(Data(clock.Now));
        await loader.Completion.Task;
    }

    [Fact]
    public async Task Generation_expiry_does_not_trigger_hidden_search_load()
    {
        var loader = new Loader();
        var clock = new Clock();
        loader.Completion.SetResult(Data(clock.Now));
        var service = new RailReferenceService(new Profile(), loader, clock);
        var result = (
            await service.SuggestAsync("Стан", TestContext.Current.CancellationToken)
        ).Value;
        var request = new RailRequest(
            result.Suggestions[0].Reference,
            result.Suggestions[1].Reference,
            result.CatalogueGeneration!,
            "2026-10-20",
            "all"
        );
        service.Resolve(request).IsError.ShouldBeFalse();
        clock.Now = clock.Now.AddMinutes(31);
        service.Resolve(request).FirstError.Code.ShouldBe("Rail.StationSelection.Expired");
        loader.Calls.ShouldBe(1);
    }
}
