using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2CompactWireTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Duffel_keeps_one_immutable_utf8_payload_for_both_aliases(string code)
    {
        var p = new HotelM2LoadProfile(code, new(2026, 11, 6));
        using var wire = new HotelM2MemoryFixture.OwnedDuffelWire
        {
            LoadProfile = p,
            Clock = new FakeTimeProvider(p.FixtureEpoch),
        };
        using var client = new HttpMessageInvoker(wire, false);
        using var search = new HttpRequestMessage(
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
                        guests = Enumerable
                            .Range(0, p.Adults)
                            .Select(_ => new { type = "adult" })
                            .ToArray(),
                    },
                }
            ),
        };
        using var reply = await client.SendAsync(search, TestContext.Current.CancellationToken);
        var body = JsonNode.Parse(
            await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
        var result = body["data"]!["results"]![0]!;
        var id = result["id"]!.GetValue<string>();
        var rateId = result["accommodation"]!["rooms"]![0]!["rates"]![0]!["id"]!.GetValue<string>();
        var originalName = result["accommodation"]!["name"]!.GetValue<string>();
        var retention = JsonSerializer.SerializeToElement(
            wire.CaptureMeasurements(true).Retained,
            Json
        );
        retention.TryGetProperty("storedRatePayloadBytes", out var stored).ShouldBeTrue();
        stored.GetInt64().ShouldBeGreaterThan(0);
        (stored.GetInt64() * 2).ShouldBe(retention.GetProperty("valueBytes").GetInt64());
        retention.GetProperty("rates").GetInt32().ShouldBe(2 * p.PropertiesPerSource);
        result["accommodation"]!["name"] = "local response mutation";
        await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(async _ =>
                {
                    using var rates = await client.SendAsync(
                        new(
                            HttpMethod.Get,
                            $"https://owned.invalid/stays/search_results/{id}/actions/fetch_all_rates"
                        ),
                        TestContext.Current.CancellationToken
                    );
                    var read = JsonNode.Parse(
                        await rates.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
                    )!["data"]!;
                    read["accommodation"]!["name"]!.GetValue<string>().ShouldBe(originalName);
                    read["accommodation"]!["name"] = "consumer mutation";
                })
        );
        using var quoteRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://owned.invalid/stays/quotes"
        )
        {
            Content = JsonContent.Create(new { data = new { rate_id = rateId } }),
        };
        using var quote = await client.SendAsync(
            quoteRequest,
            TestContext.Current.CancellationToken
        );
        var quoted = JsonNode.Parse(
            await quote.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!["data"]!;
        quoted["accommodation"]!["name"]!.GetValue<string>().ShouldBe(originalName);
        wire.CaptureMeasurements().Retained.Rates.ShouldBe(2 * p.PropertiesPerSource);
    }

    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Hotelbeds_retains_compact_keys_but_accepts_original_opaque_wire_key(
        string code
    )
    {
        var p = new HotelM2LoadProfile(code, new(2026, 11, 6));
        using var wire = new HotelM2MemoryFixture.OwnedHotelbedsWire { LoadProfile = p };
        using var client = new HttpMessageInvoker(wire, false);
        using var search = new HttpRequestMessage(
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
                            adults = p.Adults,
                            children = 0,
                        },
                    },
                    hotels = new { hotel = p.HotelbedsIds },
                }
            ),
        };
        using var reply = await client.SendAsync(search, TestContext.Current.CancellationToken);
        var body = JsonNode.Parse(
            await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
        var key = body["hotels"]!["hotels"]![0]!["rooms"]![0]!["rates"]![0]![
            "rateKey"
        ]!.GetValue<string>();
        key.Length.ShouldBe(p.OpaqueKeyBytes);
        var retention = JsonSerializer.SerializeToElement(
            wire.CaptureMeasurements(true).Retained,
            Json
        );
        retention.TryGetProperty("keyUtf16Bytes", out var storedKeys).ShouldBeTrue();
        storedKeys.GetInt64().ShouldBe(p.PropertiesPerSource * 64L * 2);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://owned.invalid/hotel-api/1.0/checkrates"
        )
        {
            Content = JsonContent.Create(new { rooms = new[] { new { rateKey = key } } }),
        };
        using var quote = await client.SendAsync(request, TestContext.Current.CancellationToken);
        quote.StatusCode.ShouldBe(HttpStatusCode.OK);
        var prepared = JsonNode.Parse(
            await quote.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!["hotel"]!["rooms"]![0]!["rates"]![0]!["rateKey"]!.GetValue<string>();
        prepared.Length.ShouldBe(p.OpaqueKeyBytes);
        wire.CaptureMeasurements().Retained.Rates.ShouldBe(p.PropertiesPerSource + 1);
        using var wrong = new HttpRequestMessage(
            HttpMethod.Post,
            "https://owned.invalid/hotel-api/1.0/checkrates"
        )
        {
            Content = JsonContent.Create(
                new { rooms = new[] { new { rateKey = key[..^1] + "z" } } }
            ),
        };
        await Should.ThrowAsync<KeyNotFoundException>(() =>
            client.SendAsync(wrong, TestContext.Current.CancellationToken)
        );
    }
}
