using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2CasTests
{
    [Fact]
    public async Task Two_healthy_contenders_commit_one_attempt_and_dispatch_one_actual_selected_adapter_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct);
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct);
        var capacity = new TwoContenderCapacity();
        var protector = fixture.Services.GetRequiredService<IHotelPrivateProtector>();
        var service = new HotelReservationService(
            fixture.Store,
            fixture.Services.GetServices<IHotelBookingProvider>(),
            protector,
            capacity,
            fixture.Clock,
            reviewedMap: fixture.ReviewedMap
        );
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var purchase = new HotelPurchase(Guid.NewGuid(), quote.Revision, true, party);
        fixture.Store.CoordinateNextAdmissionLoads(2);
        var results = await Task.WhenAll(
            service.PurchaseAsync(Guid.Parse(HotelM2MemoryFixture.Owner), quote.Id, purchase, ct),
            service.PurchaseAsync(Guid.Parse(HotelM2MemoryFixture.Owner), quote.Id, purchase, ct)
        );
        results.ShouldAllBe(r => !r.IsError);
        results.Select(r => r.Value.AttemptId).Distinct().Count().ShouldBe(1);
        results[0].Value.AttemptId.ShouldNotBeNull();
        fixture.Creates("hotelbeds").ShouldBe(1);
        fixture.Creates("duffel-stays").ShouldBe(0);
        capacity.Acquires.ShouldBe(2);
        capacity.Releases.ShouldBe(2);
        capacity.Active.ShouldBe(0);
        // The store returns JSON-round-tripped rows. A replacement service replays without
        // providers/current approval/capacity and does not infer resend permission from restart.
        var replay = new HotelReservationService(
            fixture.Store,
            [],
            protector,
            new NoCapacityOnReplay(),
            fixture.Clock
        );
        var result = await replay.PurchaseAsync(
            Guid.Parse(HotelM2MemoryFixture.Owner),
            quote.Id,
            purchase,
            ct
        );
        result.Value.AttemptId.ShouldBe(results[0].Value.AttemptId);
        fixture.Creates("hotelbeds").ShouldBe(1);
    }

    private sealed class NoCapacityOnReplay : IHotelCapacity
    {
        public IDisposable? TryAcquire(string provider, bool recovery = false) =>
            throw new InvalidOperationException("Replay may not acquire a new permit.");
    }

    // A semantic two-contender test port isolates the CAS race. It does not change the
    // production1-active/24-starts source budget or claim a load/capacity acceptance.
    private sealed class TwoContenderCapacity : IHotelCapacity
    {
        private int acquires;
        private int releases;
        public int Acquires => acquires;
        public int Releases => releases;
        public int Active => Acquires - Releases;

        public IDisposable? TryAcquire(string provider, bool recovery = false)
        {
            Interlocked.Increment(ref acquires);
            return new Lease(() => Interlocked.Increment(ref releases));
        }

        private sealed class Lease(Action release) : IDisposable
        {
            private int disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    release();
            }
        }
    }
}
