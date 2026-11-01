using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

// Exercises the actual owned typed handlers, never Travel.Host/TestServer/SQL or a network handler.
public sealed class HotelWireMeasurementTests
{
    [Fact]
    public async Task Both_owned_handlers_report_actual_starts_and_retained_values_without_payload()
    {
        using var duffel = new HotelM2MemoryFixture.OwnedDuffelWire();
        using var hotelbeds = new HotelM2MemoryFixture.OwnedHotelbedsWire();
        duffel.Measurements.StartRecording();
        hotelbeds.Measurements.StartRecording();
        using var duffelClient = new HttpClient(duffel, disposeHandler: false);
        using var hotelbedsClient = new HttpClient(hotelbeds, disposeHandler: false);
        using var d = await duffelClient.PostAsJsonAsync(
            "https://owned.invalid/stays/search",
            new
            {
                data = new
                {
                    check_in_date = "2026-11-05",
                    check_out_date = "2026-11-07",
                    guests = new[] { new { type = "adult" } },
                },
            },
            TestContext.Current.CancellationToken
        );
        using var h = await hotelbedsClient.PostAsJsonAsync(
            "https://owned.invalid/hotel-api/1.0/hotels",
            new
            {
                stay = new { checkIn = "2026-11-05", checkOut = "2026-11-07" },
                occupancies = new[]
                {
                    new
                    {
                        rooms = 1,
                        adults = 1,
                        children = 0,
                    },
                },
            },
            TestContext.Current.CancellationToken
        );
        d.IsSuccessStatusCode.ShouldBeTrue();
        h.IsSuccessStatusCode.ShouldBeTrue();
        var ds = Capture(duffel, true);
        var hs = Capture(hotelbeds, true);
        ds.GetProperty("provider").GetString().ShouldBe("duffel-stays");
        hs.GetProperty("provider").GetString().ShouldBe("hotelbeds");
        foreach (var value in new[] { ds, hs })
        {
            value.GetProperty("starts").GetInt32().ShouldBe(1);
            value.GetProperty("completions").GetInt32().ShouldBe(1);
            value.GetProperty("active").GetInt32().ShouldBe(0);
            value.GetProperty("peakActive").GetInt32().ShouldBe(1);
            value.GetProperty("events").GetArrayLength().ShouldBe(2);
            value
                .GetProperty("retained")
                .GetProperty("valueBytes")
                .GetInt64()
                .ShouldBeGreaterThan(0);
            value.GetRawText().ShouldNotContain("owned-hbx-");
            value.GetRawText().ShouldNotContain("2026-11-05");
        }
        ds.GetProperty("retained").GetProperty("rates").GetInt32().ShouldBe(2);
        hs.GetProperty("retained").GetProperty("rates").GetInt32().ShouldBe(1);
        Capture(duffel, false).GetProperty("events").GetArrayLength().ShouldBe(0);
        Capture(hotelbeds, false)
            .GetProperty("retained")
            .GetProperty("valueBytes")
            .ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_finish_an_underlying_send_that_is_still_running()
    {
        using var wire = new HotelM2MemoryFixture.OwnedHotelbedsWire();
        using var http = new HotelbedsFictionalHttpClient(wire);
        var capacity = new HotelCapacity(Options.Create(new HotelsOptions()), TimeProvider.System);
        wire.Measurements.Capacity = capacity;
        var provider = new HotelbedsStayProvider(
            http,
            Options.Create(
                new HotelbedsOptions
                {
                    Enabled = true,
                    BookingEnabled = true,
                    Fictional = true,
                    ApiKey = "owned-fictional-key",
                    Secret = "owned-fictional-secret",
                    AccountAlias = "owned",
                    ConfigurationRevision = "1",
                    ApprovalRevision = "1",
                }
            ),
            capacity,
            TimeProvider.System
        );
        var request = new StaySearch(
            HotelSearchService.Areas[3],
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9)
            ),
            new(1)
        );
        var found = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        found.Kind.ShouldBe(ExecutionKind.Complete);
        var prepared = await provider.PrepareAsync(
            new(request, found.Items[0]),
            TestContext.Current.CancellationToken
        );
        prepared.Kind.ShouldBe(PreparationKind.Ready);
        wire.CaptureMeasurements();
        wire.Measurements.StartRecording();
        var effect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.AfterEffect = async () =>
        {
            effect.TrySetResult();
            await release.Task;
        };
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        using var permit = capacity.TryAcquire("hotelbeds");
        permit.ShouldNotBeNull();
        var pending = provider.CreateAsync(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                prepared.Stay!.Terms,
                prepared.Stay.Context,
                new(
                    [new("Fictional", "Traveller")],
                    "fictional@example.invalid",
                    "+44000000000",
                    true,
                    true
                )
            ),
            caller.Token
        );
        try
        {
            await effect.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );
            caller.Cancel();
            var running = wire.CaptureMeasurements();
            running.Starts.ShouldBe(3);
            running.Completions.ShouldBe(2);
            running.Active.ShouldBe(1);
            running.Events.Single().Capacity!.Capacity.ForegroundActive.ShouldBe(1);
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }
        var finished = wire.CaptureMeasurements();
        finished.Completions.ShouldBe(3);
        finished.Active.ShouldBe(0);
        finished.Retained.Bookings.ShouldBe(1);
        permit.Dispose();
        permit.Dispose();
        capacity.GetSourceSnapshots().Single(s => s.Provider == "hotelbeds").Releases.ShouldBe(3);
    }

    [Fact]
    public async Task Undrained_observations_overflow_explicitly_and_never_reset_to_green()
    {
        using var wire = new HotelM2MemoryFixture.OwnedHotelbedsWire();
        using var client = new HttpClient(wire, disposeHandler: false);
        wire.Measurements.StartRecording();
        for (var i = 0; i < 33; i++)
        {
            using var reply = await client.GetAsync(
                "https://owned.invalid/hotel-api/1.0/bookings/missing",
                TestContext.Current.CancellationToken
            );
            reply.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        }
        var snapshot = wire.CaptureMeasurements();
        snapshot.Starts.ShouldBe(33);
        snapshot.Completions.ShouldBe(33);
        snapshot.Events.Length.ShouldBe(64);
        snapshot.Overflow.ShouldBeTrue();
        snapshot.Active.ShouldBe(0);
        wire.CaptureMeasurements().Overflow.ShouldBeTrue();
    }

    private static JsonElement Capture(object wire, bool detailed)
    {
        var snapshot = wire switch
        {
            HotelM2MemoryFixture.OwnedDuffelWire d => d.CaptureMeasurements(detailed),
            HotelM2MemoryFixture.OwnedHotelbedsWire h => h.CaptureMeasurements(detailed),
            _ => throw new ArgumentException("An owned typed wire is required."),
        };
        return JsonSerializer.SerializeToElement(
            snapshot,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
    }
}
