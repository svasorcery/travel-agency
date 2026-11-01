using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadSeedTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Fixed_source_bound_seed_leaves_runtime_permit_windows_unspent(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        var p = new HotelM2LoadProfile(code, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7));
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct, loadProfile: p);
        var seed = await HotelM2LoadSeed.PrepareMemoryAsync(fixture, p, ct);
        var rows = (
            await fixture.Store.ListAsync(Guid.Parse(HotelM2MemoryFixture.Owner), ct)
        ).Value;
        rows.Length.ShouldBe(38);
        rows.Count(r => r.AttemptId is null).ShouldBe(24);
        rows.Count(r => r.State.Outcome == ReservationOutcome.Confirmed).ShouldBe(2);
        rows.Count(r =>
                r.State.Outcome == ReservationOutcome.Unknown && r.State.BoundBookingId is null
            )
            .ShouldBe(12);
        rows.ShouldAllBe(r =>
            r.QuoteRevision == 1
            && r.State.QuoteContractVersion == 2
            && r.State.AdmissionBinding != null
            && r.ProtectedContext != null
        );
        rows.ShouldAllBe(r => r.Terms!.Occupancy.Adults == p.Adults);
        var providers = fixture.Services.GetServices<IHotelBookingProvider>().ToArray();
        rows.ShouldAllBe(r =>
            (
                (IHotelSourceDescriptor)providers.Single(s => s.Key == r.Provider)
            ).CurrentSource.Matches(r.State.AdmissionBinding!)
        );
        fixture
            .Services.GetRequiredService<HotelCapacity>()
            .GetSourceSnapshots()
            .ShouldAllBe(s => s.Capacity.TotalStarts == 0 && s.Capacity.WindowUntil == default);
        var source = fixture.CaptureMeasurements();
        source.Single(s => s.Provider == "duffel-stays").Starts.ShouldBe(40);
        source.Single(s => s.Provider == "hotelbeds").Starts.ShouldBe(16);
        source.ShouldAllBe(s => s.UnknownEffects == 0 && s.Active == 0 && !s.Overflow);
        var proofType = typeof(HotelM2LoadProfile).Assembly.GetType(
            "Travel.Modules.Hotels.Tests.Integration.HotelM2LoadCacheProof"
        );
        proofType
            ?.GetMethod("Run")
            ?.Invoke(
                null,
                [p, fixture.Services.GetRequiredService<HotelSnapshotCache>(), seed, fixture.Clock]
            );
        fixture.Services.GetRequiredService<HotelSnapshotCache>().Count.ShouldBe(128);
    }
}
