using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

// Explicit standalone TestServer/in-memory fixture. No DB, migrations, real Host or supplier calls.
public sealed class HotelM2MemoryHttpTests
{
    [Fact]
    public async Task Cached_Duffel_selection_cannot_prepare_after_credential_identity_changes()
    {
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            TestContext.Current.CancellationToken
        );
        var ct = TestContext.Current.CancellationToken;
        var prepared = await Quote(fixture, ct);
        var before = fixture.Preparations("duffel-stays");
        fixture.DuffelConfiguration.ApiKey = "duffel_test_different_owned_key";
        var newId = Guid.NewGuid();
        using var result = await fixture.Client.PutAsJsonAsync(
            $"/api/hotels/reservations/{newId}/quote",
            new
            {
                ratesId = prepared.RatesId,
                selectionKey = prepared.Alternate,
                expectedRevision = (int?)null,
            },
            ct
        );
        result.IsSuccessStatusCode.ShouldBeFalse();
        fixture.Preparations("duffel-stays").ShouldBe(before);
        fixture.Store.Snapshot(newId).ShouldBeNull();
    }

    [Fact]
    public async Task Private_routes_enforce_owner_and_scope_before_effects()
    {
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            TestContext.Current.CancellationToken
        );
        var ct = TestContext.Current.CancellationToken;
        var prepared = await Quote(fixture, ct);
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        (
            await fixture.Client.GetAsync($"/api/hotels/reservations/{prepared.Id}", ct)
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-foreign"
        );
        (
            await fixture.Client.GetAsync($"/api/hotels/reservations/{prepared.Id}", ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-no-scope"
        );
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{prepared.Id}/purchase",
                Purchase(prepared.Revision),
                ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fixture.Creates("hotelbeds").ShouldBe(0);
        fixture.Store.Snapshot(prepared.Id)!.AttemptId.ShouldBeNull();
    }

    [Theory]
    [InlineData("missing-binding")]
    [InlineData("credential")]
    [InlineData("mapping-evidence")]
    public async Task Retained_quote_revalidates_authority_before_a_new_claim(string change)
    {
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            TestContext.Current.CancellationToken
        );
        var ct = TestContext.Current.CancellationToken;
        var prepared = await Quote(fixture, ct);
        if (change == "credential")
            fixture.HotelbedsConfiguration.ApiKey = "different-owned-fictional-key";
        else if (change == "mapping-evidence")
            fixture.ReviewedMap.Pairs[0] = fixture.ReviewedMap.Pairs[0] with
            {
                Evidence = "Different owned reviewed building",
            };
        else
        {
            var row = fixture.Store.Snapshot(prepared.Id)!;
            row.State.AdmissionBinding = null;
            (await fixture.Store.SaveAsync(row, row.Version, [], ct)).Value.ShouldBeTrue();
        }
        using var result = await fixture.Client.PostAsJsonAsync(
            $"/api/hotels/reservations/{prepared.Id}/purchase",
            Purchase(prepared.Revision),
            ct
        );
        result.IsSuccessStatusCode.ShouldBeFalse();
        fixture.Store.Snapshot(prepared.Id)!.AttemptId.ShouldBeNull();
        fixture.Creates("hotelbeds").ShouldBe(0);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("validation-400")]
    public async Task Unknown_without_reference_keeps_spent_claim_and_never_reads_or_replaces_source(
        string scenario
    )
    {
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            TestContext.Current.CancellationToken
        );
        var ct = TestContext.Current.CancellationToken;
        var prepared = await Quote(fixture, ct);
        (
            await fixture.Client.PostAsJsonAsync(
                "/__owned-fixture/scenario",
                new { next = scenario },
                ct
            )
        ).EnsureSuccessStatusCode();
        var purchase = Purchase(prepared.Revision);
        using var first = await fixture.Client.PostAsJsonAsync(
            $"/api/hotels/reservations/{prepared.Id}/purchase",
            purchase,
            ct
        );
        first.EnsureSuccessStatusCode();
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync(ct));
        firstJson.RootElement.GetProperty("outcome").GetString().ShouldBe("Unknown");
        fixture.Creates("hotelbeds").ShouldBe(1);
        fixture.Reads("hotelbeds").ShouldBe(0);
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{prepared.Id}/purchase",
                Purchase(prepared.Revision),
                ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (
            await fixture.Client.PostAsJsonAsync(
                $"/api/hotels/reservations/{prepared.Id}/purchase",
                purchase,
                ct
            )
        ).EnsureSuccessStatusCode();
        (
            await fixture.Client.PutAsJsonAsync(
                $"/api/hotels/reservations/{prepared.Id}/quote",
                new
                {
                    ratesId = prepared.RatesId,
                    selectionKey = prepared.Alternate,
                    expectedRevision = prepared.Revision,
                },
                ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        fixture.Creates("hotelbeds").ShouldBe(1);
        fixture.Creates("duffel-stays").ShouldBe(0);
        fixture.Reads("hotelbeds").ShouldBe(0);
    }

    internal static object Purchase(int revision) =>
        new
        {
            actionId = Guid.NewGuid(),
            quoteRevision = revision,
            consent = true,
            party = new
            {
                guests = new[] { new { givenName = "Fictional", familyName = "Traveller" } },
                email = "fictional@example.invalid",
                phone = "+44000000000",
                adultsConfirmed = true,
                checkInConfirmed = true,
            },
        };

    internal static async Task<(Guid Id, int Revision, Guid RatesId, string Alternate)> Quote(
        HotelM2MemoryFixture fixture,
        CancellationToken ct,
        string provider = "hotelbeds"
    )
    {
        var dates = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        using var searchResponse = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn = dates,
                checkOut = dates.AddDays(2),
                adults = 1,
            },
            ct
        );
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(ct));
        using var ratesResponse = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/rates",
            new
            {
                searchId = search.RootElement.GetProperty("id").GetGuid(),
                propertyGroupKey = search
                    .RootElement.GetProperty("groups")[0]
                    .GetProperty("key")
                    .GetString(),
            },
            ct
        );
        using var rates = JsonDocument.Parse(await ratesResponse.Content.ReadAsStringAsync(ct));
        var ratesId = rates.RootElement.GetProperty("id").GetGuid();
        var all = rates
            .RootElement.GetProperty("result")
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        var selected = all.Single(o => o.GetProperty("provider").GetString() == provider)
            .GetProperty("selectionKey")
            .GetString()!;
        var alternate = all.Single(o => o.GetProperty("provider").GetString() != provider)
            .GetProperty("selectionKey")
            .GetString()!;
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-owner"
        );
        var id = Guid.NewGuid();
        using var quoteResponse = await fixture.Client.PutAsJsonAsync(
            $"/api/hotels/reservations/{id}/quote",
            new
            {
                ratesId,
                selectionKey = selected,
                expectedRevision = (int?)null,
            },
            ct
        );
        quoteResponse.EnsureSuccessStatusCode();
        using var quote = JsonDocument.Parse(await quoteResponse.Content.ReadAsStringAsync(ct));
        return (id, quote.RootElement.GetProperty("quoteRevision").GetInt32(), ratesId, alternate);
    }

    [Theory]
    [InlineData("hotelbeds", 1)]
    [InlineData("hotelbeds", 2)]
    [InlineData("duffel-stays", 1)]
    public async Task Both_real_adapters_use_owned_wire_and_selected_source_books_once(
        string provider,
        int adults
    )
    {
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            TestContext.Current.CancellationToken
        );
        var ct = TestContext.Current.CancellationToken;
        var dates = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        using var search = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn = dates,
                checkOut = dates.AddDays(2),
                adults,
            },
            ct
        );
        search.StatusCode.ShouldBe(HttpStatusCode.OK);
        search.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var searchJson = JsonDocument.Parse(await search.Content.ReadAsStringAsync(ct));
        searchJson.RootElement.GetProperty("statuses").GetArrayLength().ShouldBe(2);
        searchJson.RootElement.GetProperty("groups").GetArrayLength().ShouldBe(1);
        var group = searchJson.RootElement.GetProperty("groups")[0];
        using var rates = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/rates",
            new
            {
                searchId = searchJson.RootElement.GetProperty("id").GetGuid(),
                propertyGroupKey = group.GetProperty("key").GetString(),
            },
            ct
        );
        rates.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ratesJson = JsonDocument.Parse(await rates.Content.ReadAsStringAsync(ct));
        var rate = ratesJson
            .RootElement.GetProperty("result")
            .GetProperty("items")
            .EnumerateArray()
            .Single(o => o.GetProperty("provider").GetString() == provider);
        var id = Guid.NewGuid();
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-owner"
        );
        using var quote = await fixture.Client.PutAsJsonAsync(
            $"/api/hotels/reservations/{id}/quote",
            new
            {
                ratesId = ratesJson.RootElement.GetProperty("id").GetGuid(),
                selectionKey = rate.GetProperty("selectionKey").GetString(),
                expectedRevision = (int?)null,
            },
            ct
        );
        quote.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var q = JsonDocument.Parse(await quote.Content.ReadAsStringAsync(ct));
        using var purchase = await fixture.Client.PostAsJsonAsync(
            $"/api/hotels/reservations/{id}/purchase",
            new
            {
                actionId = Guid.NewGuid(),
                quoteRevision = q.RootElement.GetProperty("quoteRevision").GetInt32(),
                consent = true,
                party = new
                {
                    guests = Enumerable
                        .Range(0, adults)
                        .Select(_ => new { givenName = "Fictional", familyName = "Traveller" })
                        .ToArray(),
                    email = "fictional@example.invalid",
                    phone = "+44000000000",
                    adultsConfirmed = true,
                    checkInConfirmed = true,
                },
            },
            ct
        );
        purchase.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var outcome = JsonDocument.Parse(await purchase.Content.ReadAsStringAsync(ct));
        outcome.RootElement.GetProperty("outcome").GetString().ShouldBe("Confirmed");
        outcome.RootElement.GetProperty("provider").GetString().ShouldBe(provider);
        using var reloaded = await fixture.Client.GetAsync($"/api/hotels/reservations/{id}", ct);
        reloaded.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.Creates(provider).ShouldBe(1);
        fixture.Creates(provider == "hotelbeds" ? "duffel-stays" : "hotelbeds").ShouldBe(0);
        fixture.Store.Snapshot(id)!.Provider.ShouldBe(provider);
        if (provider == "duffel-stays")
            fixture.Store.Snapshot(id)!.ProviderAccount.ShouldBe("org_owned_transport");
    }
}
