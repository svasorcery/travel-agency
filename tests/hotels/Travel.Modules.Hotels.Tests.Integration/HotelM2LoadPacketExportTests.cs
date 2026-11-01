using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Hotels.Fixture;
using Travel.Modules.Hotels.Application;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadPacketExportTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Canonical_payloads_private_contexts_seed_and_schedule_fit_declared_envelopes(
        string code
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = new HotelM2LoadProfile(code, today.AddDays(7));
        var epoch = new DateTimeOffset(
            today.ToDateTime(new TimeOnly(12, 0)),
            TimeSpan.Zero
        ).AddTicks(1234567);
        var artifacts = new Dictionary<string, byte[]>();
        var clock = new FakeTimeProvider(epoch);
        await using (
            var fixture = await HotelM2MemoryFixture.StartAsync(
                ct,
                time: clock,
                loadProfile: profile
            )
        )
        {
            using var d = new HttpMessageInvoker(fixture.DuffelWire, false);
            using var h = new HttpMessageInvoker(fixture.HotelbedsWire, false);
            using var dr = new HttpRequestMessage(
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
                            check_in_date = HotelM2LoadProfile.Date(profile.CheckIn),
                            check_out_date = HotelM2LoadProfile.Date(profile.CheckOut),
                            guests = Enumerable
                                .Range(0, profile.Adults)
                                .Select(_ => new { type = "adult" })
                                .ToArray(),
                        },
                    }
                ),
            };
            using var hr = new HttpRequestMessage(
                HttpMethod.Post,
                "https://owned.invalid/hotel-api/1.0/hotels"
            )
            {
                Content = JsonContent.Create(
                    new
                    {
                        stay = new
                        {
                            checkIn = HotelM2LoadProfile.Date(profile.CheckIn),
                            checkOut = HotelM2LoadProfile.Date(profile.CheckOut),
                        },
                        occupancies = new[]
                        {
                            new
                            {
                                rooms = 1,
                                adults = profile.Adults,
                                children = 0,
                            },
                        },
                        hotels = new { hotel = profile.HotelbedsIds },
                    }
                ),
            };
            using var draw = await d.SendAsync(dr, ct);
            using var hraw = await h.SendAsync(hr, ct);
            artifacts.Add("duffel-availability.json", await draw.Content.ReadAsByteArrayAsync(ct));
            artifacts.Add(
                "hotelbeds-availability.json",
                await hraw.Content.ReadAsByteArrayAsync(ct)
            );
            using var searchResponse = await fixture.Client.PostAsJsonAsync(
                "/api/hotels/search",
                new
                {
                    areaKey = "fictional",
                    checkIn = profile.CheckIn,
                    checkOut = profile.CheckOut,
                    adults = profile.Adults,
                },
                ct
            );
            searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var publicBytes = await searchResponse.Content.ReadAsByteArrayAsync(ct);
            var value = JsonSerializer.Deserialize<HotelSearchSnapshot>(
                publicBytes,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )!;
            HotelsM2LoadDispatch.HealthySearch(value, profile).ShouldBeTrue();
            artifacts.Add("public-search.json", publicBytes);
            var saved = fixture.Services.GetRequiredService<HotelSnapshotCache>().Get(value.Id)!;
            saved.Result.Items.ShouldAllBe(o =>
                o.SelectionContext != null
                && Encoding.UTF8.GetByteCount(o.SelectionContext.Value) <= 4096
            );
            var privateBytes = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    handles = saved.Result.Items.Select(o => new
                    {
                        o.SelectionKey,
                        o.SelectionContext,
                        o.AdmissionBinding,
                    }),
                    mappings = saved.Groups.Select(g => g.Mapping),
                }
            );
            artifacts.Add("private-search-specimen.json", privateBytes);
            Encoding.UTF8.GetString(publicBytes).ShouldNotContain("hbx_load_");
            fixture
                .Services.GetRequiredService<HotelSnapshotCache>()
                .PayloadBytes.ShouldBeLessThanOrEqualTo(2 * 1024 * 1024);
        }
        HotelM2CacheProof proof;
        HotelM2SeedResult seed;
        await using (
            var fixture = await HotelM2MemoryFixture.StartAsync(
                ct,
                time: new FakeTimeProvider(epoch),
                loadProfile: profile
            )
        )
        {
            seed = await HotelM2LoadSeed.PrepareMemoryAsync(fixture, profile, ct);
            proof = HotelM2LoadCacheProof.Run(
                profile,
                fixture.Services.GetRequiredService<HotelSnapshotCache>(),
                seed,
                fixture.Clock
            );
        }
        artifacts.Add(
            "schedule.json",
            JsonSerializer.SerializeToUtf8Bytes(new HotelM2LoadSchedule(profile))
        );
        artifacts.Add("cache-proof.json", JsonSerializer.SerializeToUtf8Bytes(proof));
        artifacts.Add("seed.json", JsonSerializer.SerializeToUtf8Bytes(seed));
        foreach (
            var name in new[]
            {
                "duffel-availability.json",
                "hotelbeds-availability.json",
                "public-search.json",
                "private-search-specimen.json",
            }
        )
            artifacts[name].Length.ShouldBeLessThanOrEqualTo(2 * 1024 * 1024);
        var inventory = artifacts
            .Select(p => new
            {
                file = p.Key,
                bytes = p.Value.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(p.Value)),
            })
            .ToArray();
        var metadata = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                profile,
                canonicalEpoch = epoch,
                inventory,
                seedSourceSteps = new { duffel = 40, hotelbeds = 16 },
                runtimeAdmissionStarts = 0,
                note = "Canonical producer specimens. Live GUIDs/expiry/key ordinals/ciphertexts vary; these hashes pin the reviewed data/code shape, not future response bytes.",
            }
        );
        artifacts.Add("payload-inventory.json", metadata);
        var directory = Environment.GetEnvironmentVariable("HOTELS_M2_PROFILE_EXPORT");
        if (string.IsNullOrEmpty(directory))
            return;
        var full = Path.GetFullPath(Path.Combine(directory, code));
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "Travel.slnx")))
            repo = repo.Parent;
        repo.ShouldNotBeNull();
        full.StartsWith(
                Path.Combine(repo.FullName, "TestResults", "hotels-m2-memory-load-acceptance")
                    + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            )
            .ShouldBeTrue();
        Directory.Exists(full).ShouldBeFalse();
        Directory.CreateDirectory(full);
        foreach (var artifact in artifacts)
        {
            await using var file = new FileStream(
                Path.Combine(full, artifact.Key),
                FileMode.CreateNew
            );
            await file.WriteAsync(artifact.Value, ct);
        }
    }
}
