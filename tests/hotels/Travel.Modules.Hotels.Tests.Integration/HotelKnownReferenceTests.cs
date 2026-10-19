using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelKnownReferenceTests
{
    [Theory]
    [InlineData("hotelbeds")]
    [InlineData("duffel-stays")]
    public async Task M2_off_blocks_a_draft_but_matching_admitted_reader_still_drains(
        string provider
    )
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct);
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct, provider);
        fixture.DuffelConfiguration.Aggregation.Enabled = false;
        using var refused = await fixture.Client.PostAsJsonAsync(
            $"/api/hotels/reservations/{quote.Id}/purchase",
            HotelM2MemoryHttpTests.Purchase(quote.Revision),
            ct
        );
        refused.IsSuccessStatusCode.ShouldBeFalse();
        fixture.Store.Snapshot(quote.Id)!.AttemptId.ShouldBeNull();
        fixture.Creates(provider).ShouldBe(0);

        fixture.DuffelConfiguration.Aggregation.Enabled = true;
        if (provider == "hotelbeds")
            fixture.ReturnHotelbedsReferenceWithoutDetails();
        else
            fixture.ReturnDuffelReferenceWithoutDetails();
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{quote.Id}/purchase",
                HotelM2MemoryHttpTests.Purchase(quote.Revision),
                ct
            )
        ).EnsureSuccessStatusCode();
        fixture
            .Store.Snapshot(quote.Id)!
            .State.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        fixture.DuffelConfiguration.Aggregation.Enabled = false;
        fixture.HotelbedsConfiguration.BookingEnabled = false;
        fixture.DuffelConfiguration.BookingEnabled = false;
        await Read(fixture, quote.Id, ct);
        fixture.Reads(provider).ShouldBe(1);
        fixture.Creates(provider).ShouldBe(1);
        fixture.Store.Snapshot(quote.Id)!.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
    }

    [Fact]
    public async Task Three_failed_known_reference_reads_keep_the_original_five_minute_window_across_scopes()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct, time: time);
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct);
        fixture.ReturnHotelbedsReferenceWithoutDetails();
        fixture.SetHotelbedsReadScenario(true);
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{quote.Id}/purchase",
                HotelM2MemoryHttpTests.Purchase(quote.Revision),
                ct
            )
        ).EnsureSuccessStatusCode();
        var until = fixture.Store.Snapshot(quote.Id)!.State.ReadUntil;
        for (var i = 0; i < 3; i++)
        {
            await Read(fixture, quote.Id, ct);
            if (i == 0)
                time.Advance(TimeSpan.FromSeconds(31));
            if (i == 1)
                time.Advance(TimeSpan.FromSeconds(121));
        }
        await Read(fixture, quote.Id, ct);
        fixture.Reads("hotelbeds").ShouldBe(3);
        var saved = fixture.Store.Snapshot(quote.Id)!;
        saved.State.ReadsClaimed.ShouldBe(3);
        saved.State.ReadUntil.ShouldBe(until);
        saved.State.EverReserved.ShouldBeTrue();
        saved.AttemptId.ShouldNotBeNull();
        fixture.Creates("hotelbeds").ShouldBe(1);
    }

    [Fact]
    public async Task Changed_credential_identity_prevents_known_reference_HTTP_without_spending_read_allowance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct);
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct);
        fixture.ReturnHotelbedsReferenceWithoutDetails();
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{quote.Id}/purchase",
                HotelM2MemoryHttpTests.Purchase(quote.Revision),
                ct
            )
        ).EnsureSuccessStatusCode();
        fixture.HotelbedsConfiguration.ApiKey = "different-owned-key";
        await Read(fixture, quote.Id, ct);
        fixture.Reads("hotelbeds").ShouldBe(0);
        var saved = fixture.Store.Snapshot(quote.Id)!;
        saved.State.ReadsClaimed.ShouldBe(0);
        saved.State.Review.ShouldContain(ReviewReason.ObservationUnavailable);
        saved.State.EverReserved.ShouldBeTrue();
    }

    [Fact]
    public async Task Actual_known_reference_mismatch_preserves_positive_reservation_without_another_purchase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct);
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct);
        fixture.ReturnHotelbedsReferenceWithoutDetails();
        fixture.SetHotelbedsReadScenario(false, 999m);
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{quote.Id}/purchase",
                HotelM2MemoryHttpTests.Purchase(quote.Revision),
                ct
            )
        ).EnsureSuccessStatusCode();
        await Read(fixture, quote.Id, ct);
        var saved = fixture.Store.Snapshot(quote.Id)!;
        saved.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        saved.State.Review.ShouldContain(ReviewReason.TermsMismatch);
        saved.State.FirstMismatch!.Actual.Total.Amount.ShouldBe(999m);
        fixture.Creates("hotelbeds").ShouldBe(1);
        fixture.Creates("duffel-stays").ShouldBe(0);
    }

    private static async Task Read(HotelM2MemoryFixture fixture, Guid id, CancellationToken ct)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<HotelObservationRunner>()
            .RunAsync(new(id, false, fixture.Clock.GetUtcNow()), ct);
    }
}
