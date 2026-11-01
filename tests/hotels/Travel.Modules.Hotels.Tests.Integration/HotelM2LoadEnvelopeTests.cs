using System.Net.Http.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadEnvelopeTests
{
    [Fact]
    public async Task Wrong_supplier_catalog_is_a_sticky_harness_failure()
    {
        var p = new HotelM2LoadProfile("S", new(2026, 11, 6));
        var control = new HotelM2LoadControl(p, new FakeTimeProvider());
        using var wire = new HotelM2MemoryFixture.OwnedHotelbedsWire
        {
            LoadProfile = p,
            LoadControl = control,
        };
        using var http = new HttpMessageInvoker(wire, false);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://owned.invalid/hotel-api/1.0/hotels"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    stay = new { checkIn = "2026-11-06", checkOut = "2026-11-08" },
                    occupancies = new[]
                    {
                        new
                        {
                            rooms = 1,
                            adults = 1,
                            children = 0,
                        },
                    },
                    hotels = new { hotel = new[] { 900001 } },
                }
            ),
        };
        await Should.ThrowAsync<InvalidOperationException>(() =>
            http.SendAsync(request, TestContext.Current.CancellationToken)
        );
        control.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task Retained_fixture_overflow_is_not_reported_as_a_product_429()
    {
        var ct = TestContext.Current.CancellationToken;
        var p = new HotelM2LoadProfile("S", new(2026, 11, 6));
        var control = new HotelM2LoadControl(p, new FakeTimeProvider());
        using var wire = new HotelM2MemoryFixture.OwnedDuffelWire
        {
            LoadProfile = p,
            LoadControl = control,
        };
        using var http = new HttpMessageInvoker(wire, false);
        async Task Send()
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://owned.invalid/stays/search"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        data = new
                        {
                            rooms = 1,
                            check_in_date = "2026-11-06",
                            check_out_date = "2026-11-08",
                            guests = new[] { new { type = "adult" } },
                        },
                    }
                ),
            };
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        for (var i = 0; i < 170; i++)
            await Send();
        await Should.ThrowAsync<InvalidOperationException>(Send);
        control.Failure.ShouldNotBeNull();
        wire.CaptureMeasurements().Retained.Rates.ShouldBe(p.MaximumDuffelRateEntries);
    }
}
