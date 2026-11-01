using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

// Contract capture only: existing TestServer, owned transports, in-memory store and fake clock.
// No Kestrel/Travel.Host, database, dump collector, real-time wait or external request.
public sealed class HotelDiagnosticJsonContractTests
{
    [Fact]
    public async Task Search_wire_statuses_and_expired_rates_are_diagnostic_inputs()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(ct, time: clock);
        var checkIn = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(7);
        var body = new
        {
            areaKey = "fictional",
            checkIn,
            checkOut = checkIn.AddDays(2),
            adults = 1,
            rooms = 1,
            children = 0,
        };
        var captures = new Dictionary<string, string>();
        var ids = new HashSet<Guid>();
        Guid lastId = default;
        string lastGroup = "";
        for (var i = 0; i < 8; i++)
        {
            using var response = await fixture.Client.PostAsJsonAsync(
                "/api/hotels/search",
                body,
                ct
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            root.GetProperty("result").GetProperty("kind").GetString().ShouldBe("Complete");
            root.GetProperty("groups").GetArrayLength().ShouldBe(1);
            root.GetProperty("groups")[0].GetProperty("offers").GetArrayLength().ShouldBe(2);
            foreach (var status in root.GetProperty("statuses").EnumerateArray())
            {
                status.GetProperty("kind").ValueKind.ShouldBe(JsonValueKind.String);
                status.GetProperty("kind").GetString().ShouldBe("Complete");
                status.GetProperty("attempted").GetBoolean().ShouldBeTrue();
                status.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
                status.GetProperty("resultCount").GetInt32().ShouldBe(1);
            }
            lastId = root.GetProperty("id").GetGuid();
            ids.Add(lastId).ShouldBeTrue();
            lastGroup = root.GetProperty("groups")[0].GetProperty("key").GetString()!;
            captures.Add($"search-{i:00}.json", json);
        }
        var before = fixture.CaptureMeasurements();
        clock.Advance(TimeSpan.FromSeconds(302));
        using var expired = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/rates",
            new { searchId = lastId, propertyGroupKey = lastGroup },
            ct
        );
        expired.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var expiredJson = await expired.Content.ReadAsStringAsync(ct);
        using var error = JsonDocument.Parse(expiredJson);
        error
            .RootElement.GetProperty("type")
            .GetString()
            .ShouldBe("https://travel.local/errors/Hotels.Expired");
        error.RootElement.GetProperty("status").GetInt32().ShouldBe(409);
        fixture.CaptureMeasurements().Select(s => s.Starts).ShouldBe(before.Select(s => s.Starts));
        fixture.Creates("duffel-stays").ShouldBe(0);
        fixture.Creates("hotelbeds").ShouldBe(0);
        captures.Add("expired.json", expiredJson);
        captures.Add("request.json", JsonSerializer.Serialize(body));
        captures.Add(
            "capture.json",
            JsonSerializer.Serialize(
                new
                {
                    kind = "standalone-api-contract-capture",
                    clockStartUtc = clock.GetUtcNow().AddSeconds(-302),
                    requests = 9,
                    realTimeWait = false,
                    fakeClockAdvanceSeconds = 302,
                    database = false,
                    kestrel = false,
                    externalNetwork = false,
                    dumpCollector = false,
                }
            )
        );
        var destination = Environment.GetEnvironmentVariable("HOTELS_DIAGNOSTIC_CONTRACT_CAPTURE");
        if (string.IsNullOrEmpty(destination))
            return;
        var full = Path.GetFullPath(destination);
        var rootDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            rootDirectory is not null
            && !File.Exists(Path.Combine(rootDirectory.FullName, "Travel.slnx"))
        )
            rootDirectory = rootDirectory.Parent;
        rootDirectory.ShouldNotBeNull();
        var boundary =
            Path.GetFullPath(
                Path.Combine(
                    rootDirectory.FullName,
                    "TestResults",
                    "hotels-m2-memory-load-acceptance"
                )
            ) + Path.DirectorySeparatorChar;
        full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        Directory.Exists(full).ShouldBeFalse();
        Directory.CreateDirectory(full);
        foreach (var item in captures)
        {
            await using var file = new FileStream(Path.Combine(full, item.Key), FileMode.CreateNew);
            await file.WriteAsync(Encoding.UTF8.GetBytes(item.Value), ct);
        }
    }
}
