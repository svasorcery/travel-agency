using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Hotels.Fixture;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2EvidenceConsumerTests
{
    [Fact]
    public async Task Durable_confirmation_from_real_owned_flow_reaches_phase_and_window_report()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct);
        fixture.ReturnHotelbedsReferenceWithoutDetails();
        var quote = await HotelM2MemoryHttpTests.Quote(fixture, ct);
        var evidence = new HotelsM2Evidence([new(0, "create", 284.5, "hotelbeds")]);
        evidence.Boundary("beforeTimed", fixture.CaptureMeasurements(true));
        fixture.StartMeasurements();
        using var purchase = await fixture.Client.PostAsJsonAsync(
            $"/api/hotels/reservations/{quote.Id}/purchase",
            HotelM2MemoryHttpTests.Purchase(quote.Revision),
            ct
        );
        purchase.IsSuccessStatusCode.ShouldBeTrue();
        using var reply = JsonDocument.Parse(await purchase.Content.ReadAsStringAsync(ct));
        evidence.RecordHttp(
            0,
            quote.Id,
            reply.RootElement.GetProperty("provider").GetString(),
            285,
            (int)purchase.StatusCode,
            true
        );
        fixture
            .Store.Snapshot(quote.Id)!
            .State.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        var pendingRows = await fixture.CapturePurchasesAsync(ct);
        evidence.Sample(299, fixture.CaptureMeasurements(), pendingRows);
        using var scope = fixture.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<HotelObservationRunner>()
            .RunAsync(new(quote.Id, false, fixture.Clock.GetUtcNow()), ct);
        fixture.Store.Snapshot(quote.Id)!.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        var confirmedRows = await fixture.CapturePurchasesAsync(ct);
        evidence.Sample(301, fixture.CaptureMeasurements(), confirmedRows);
        evidence.Boundary("afterTimed", fixture.CaptureMeasurements(true));

        evidence.PhasePurchaseSuccess(0, 240, 300).ShouldBeFalse();
        evidence.WindowPurchaseSuccess(0, 300).ShouldBeFalse();
        evidence.WindowPurchaseSuccess(0, 305).ShouldBeTrue();
        var result = JsonSerializer.SerializeToElement(evidence.Report(), Json);
        var fact = result.GetProperty("purchases")[0];
        fact.GetProperty("effects").GetInt32().ShouldBe(1);
        fact.GetProperty("sourceMatched").GetBoolean().ShouldBeTrue();
        fact.GetProperty("authoritativeObservedAt").GetDouble().ShouldBe(301);
        result.GetRawText().ShouldNotContain("fictional@example.invalid");
        result.GetRawText().ShouldNotContain("owned-hbx-");
    }

    [Fact]
    public async Task Successful_run_persists_all361_source_points_including_terminal_events_and_boundary_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var duffel = new HotelM2MemoryFixture.OwnedDuffelWire();
        using var hbx = new HotelM2MemoryFixture.OwnedHotelbedsWire();
        using var client = new HttpClient(hbx, disposeHandler: false);
        HotelWireSnapshot[] Capture(bool detailed = false) =>
            [duffel.CaptureMeasurements(detailed), hbx.CaptureMeasurements(detailed)];
        var evidence = new HotelsM2Evidence([new(0, "status", 0)]);
        evidence.Boundary("beforeTimed", Capture(true));
        duffel.Measurements.StartRecording();
        hbx.Measurements.StartRecording();
        for (var second = 0; second <= 360; second++)
        {
            if (second == 360)
            {
                using var response = await client.PostAsJsonAsync(
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
                    ct
                );
                response.IsSuccessStatusCode.ShouldBeTrue();
            }
            evidence.Sample(second, Capture(), []);
        }
        evidence.Boundary("afterTimed", Capture(true));
        var directory = Path.Combine(
            Path.GetTempPath(),
            "hotels-evidence-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "source-measurements.json");
        try
        {
            await evidence.SaveAsync(file, ct);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
            saved.RootElement.GetProperty("completeEvidence").GetBoolean().ShouldBeTrue();
            var samples = saved.RootElement.GetProperty("samples");
            samples.GetArrayLength().ShouldBe(361);
            var last = samples[360];
            last.GetProperty("at").GetDouble().ShouldBe(360);
            var source = last.GetProperty("sources")
                .EnumerateArray()
                .Single(s => s.GetProperty("provider").GetString() == "hotelbeds");
            source.GetProperty("events").GetArrayLength().ShouldBe(2);
            source.GetProperty("retained").GetProperty("rates").GetInt32().ShouldBe(1);
            var final = saved.RootElement.GetProperty("boundaries").GetProperty("afterTimed")[1];
            final
                .GetProperty("retained")
                .GetProperty("valueBytes")
                .GetInt64()
                .ShouldBeGreaterThan(0);
            final.GetProperty("effectBytes").ValueKind.ShouldBe(JsonValueKind.Number);
            await Should.ThrowAsync<IOException>(() => evidence.SaveAsync(file, ct));
        }
        finally
        {
            Path.GetDirectoryName(Path.GetFullPath(directory))
                .ShouldBe(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())));
            Path.GetFileName(directory).ShouldStartWith("hotels-evidence-test-");
            if (File.Exists(file))
                File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
