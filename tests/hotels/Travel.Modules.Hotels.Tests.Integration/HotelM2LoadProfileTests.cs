using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadProfileTests
{
    [Theory]
    [InlineData("S", 1, 10, 18, 8, 64)]
    [InlineData("L", 2, 100, 200, 100, 2048)]
    public async Task Profile_uses_actual_aggregation_private_handles_and_both_purchase_paths(
        string code,
        int adults,
        int groups,
        int leaves,
        int pairs,
        int keyBytes
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        await using var fixture = await Start(code, checkIn, ct);
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-owner"
        );
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn,
                checkOut = checkIn.AddDays(2),
                adults,
            },
            ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var publicJson = await response.Content.ReadAsStringAsync(ct);
        var search = JsonSerializer.Deserialize<HotelSearchSnapshot>(
            publicJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        search.Statuses.Length.ShouldBe(2);
        search.Statuses.ShouldAllBe(x => x.Kind == ExecutionKind.Complete);
        search.Groups.Length.ShouldBe(groups);
        search.Groups.Sum(x => x.Offers.Length).ShouldBe(leaves);
        search.Groups.Count(x => x.MatchReason == "Reviewed").ShouldBe(pairs);
        var hbx = search
            .Groups.SelectMany(x => x.Offers)
            .Where(x => x.Provider == "hotelbeds")
            .ToArray();
        hbx.Count(x => x.Price!.SupplierPayable!.Currency == "EUR").ShouldBe(pairs / 5);
        hbx.Count(x => x.Price!.LocalObligations is null).ShouldBe(pairs / 7);
        publicJson.ShouldNotContain("rateKey");
        publicJson.ShouldNotContain("selectionContext");
        publicJson.ShouldNotContain("hbx_load_");
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.HealthySearch(search, new(code, checkIn))
            .ShouldBeTrue();
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.HealthySearch(
                search with
                {
                    Groups = search.Groups.Reverse().ToArray(),
                },
                new(code, checkIn)
            )
            .ShouldBeFalse();
        var badGroup = search.Groups[0] with { Minima = [], PrimaryBucket = "wrong" };
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.HealthySearch(
                search with
                {
                    Groups = [badGroup, .. search.Groups.Skip(1)],
                },
                new(code, checkIn)
            )
            .ShouldBeFalse();
        var badOffer = search.Groups[0].Offers[0] with
        {
            Ranking = search.Groups[0].Offers[0].Ranking! with { Reason = "wrong" },
        };
        var badRanking = search.Groups[0] with
        {
            Offers = [badOffer, .. search.Groups[0].Offers.Skip(1)],
        };
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.HealthySearch(
                search with
                {
                    Groups = [badRanking, .. search.Groups.Skip(1)],
                },
                new(code, checkIn)
            )
            .ShouldBeFalse();
        var first = search.Groups.Single(x =>
            x.Offers.Any(o => o.Provider == "hotelbeds" && o.Terms.Property.Id == "900001")
        );
        using var ratesResponse = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/rates",
            new { searchId = search.Id, propertyGroupKey = first.Key },
            ct
        );
        ratesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rates = await ratesResponse.Content.ReadFromJsonAsync<HotelRatesSnapshot>(ct);
        rates.ShouldNotBeNull();
        rates.Result.Items.Length.ShouldBe(2);
        var saved = fixture.Services.GetRequiredService<HotelSnapshotCache>().GetRates(rates.Id)!;
        var privateHbx = saved
            .Result.Items.Single(o => o.Provider == "hotelbeds")
            .SelectionContext!;
        Encoding.UTF8.GetByteCount(privateHbx.Value).ShouldBeLessThanOrEqualTo(4096);
        using var handle = JsonDocument.Parse(privateHbx.Value);
        Encoding
            .UTF8.GetByteCount(handle.RootElement.GetProperty("rateKey").GetString()!)
            .ShouldBe(keyBytes);
        var oversizedNode = System.Text.Json.Nodes.JsonNode.Parse(privateHbx.Value)!;
        oversizedNode["rateKey"] = new string('x', 4096);
        var oversizedContext = privateHbx with { Value = oversizedNode.ToJsonString() };
        Encoding.UTF8.GetByteCount(oversizedContext.Value).ShouldBeGreaterThan(4096);
        var oversized = saved with
        {
            Id = Guid.NewGuid(),
            Result = saved.Result with
            {
                Items = saved
                    .Result.Items.Select(o =>
                        o.Provider == "hotelbeds"
                            ? o with
                            {
                                SelectionContext = oversizedContext,
                            }
                            : o
                    )
                    .ToArray(),
            },
        };
        fixture.Services.GetRequiredService<HotelSnapshotCache>().Put(oversized).ShouldBeFalse();
        foreach (var provider in new[] { "duffel-stays", "hotelbeds" })
        {
            var selection = rates.Result.Items.Single(o => o.Provider == provider);
            selection.Purchasable.ShouldBeTrue();
            var id = Guid.NewGuid();
            using var quoted = await fixture.Client.PutAsJsonAsync(
                $"/api/hotels/reservations/{id}/quote",
                new
                {
                    ratesId = rates.Id,
                    selectionKey = selection.SelectionKey,
                    expectedRevision = (int?)null,
                },
                ct
            );
            quoted.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var quote = JsonDocument.Parse(await quoted.Content.ReadAsStringAsync(ct));
            var revision = quote.RootElement.GetProperty("quoteRevision").GetInt32();
            using var purchased = await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{id}/purchase",
                new HotelPurchase(
                    Guid.NewGuid(),
                    revision,
                    true,
                    new HotelParty(
                        Enumerable
                            .Range(1, adults)
                            .Select(i => new HotelGuest("Fictional", "Traveller" + i))
                            .ToArray(),
                        "fictional@example.invalid",
                        "+44000000000",
                        true,
                        true
                    )
                ),
                ct
            );
            purchased.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var purchase = JsonDocument.Parse(await purchased.Content.ReadAsStringAsync(ct));
            purchase.RootElement.GetProperty("provider").GetString().ShouldBe(provider);
            purchase.RootElement.GetProperty("outcome").GetString().ShouldBe("Confirmed");
            fixture.Creates(provider).ShouldBe(1);
        }
    }

    private static Task<HotelM2MemoryFixture> Start(
        string code,
        DateOnly checkIn,
        CancellationToken ct
    ) => HotelM2MemoryFixture.StartAsync(ct, loadProfile: new HotelM2LoadProfile(code, checkIn));
}
