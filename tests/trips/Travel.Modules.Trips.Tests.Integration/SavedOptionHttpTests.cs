using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class SavedOptionHttpTests
{
    private const string Preview = "/api/trips/option-preview";
    private const string Save = "/api/trips/saved-options";
    private const string Header = "X-Trips-Option-Save";
    private const string Brief =
        """{"brief":{"originId":"moscow","destinationId":"saint-petersburg","startDate":"2026-10-28","endDate":"2026-10-30","adults":2,"currency":"RUB","totalBudget":null,"interests":["architecture"],"pace":"relaxed"}}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_and_history_probes_attribute_a_deliberate_blocked_port_call_to_each_request()
    {
        using var ring = new TripsTestRing();
        var store = new FixtureStore();
        var probe = new TripOptionEffectsProbe();
        await using var fixture = await Start(store, ring.Provider, probe);
        using var chosen = await Select(fixture, Brief);
        using var json = await Json(chosen);
        var selection = json.RootElement.GetProperty("selection");
        var id = selection.GetProperty("tripId").GetGuid();
        var token = selection.GetProperty("selectionToken").GetString();
        var flights = fixture.App.Services.GetRequiredService<IFlightsTripsOrderAccess>();
        store.ExerciseBlockedPort = ct =>
            Should.ThrowAsync<TripOptionEffectsProbe.BlockedTestEffect>(() =>
                flights.ListCandidatesAsync(TripsHttpFixture.Owner, 0, ct)
            );
        using var saved = await Observed(
            fixture,
            probe,
            "save-control",
            HttpMethod.Post,
            Save,
            JsonContent.Create(new { selectionToken = token }),
            expectedPort: "IFlightsTripsOrderAccess.ListCandidatesAsync"
        );
        saved.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var read = await Observed(
            fixture,
            probe,
            "history-control",
            HttpMethod.Get,
            $"/api/trips/{id}/saved-option",
            expectedPort: "IFlightsTripsOrderAccess.ListCandidatesAsync"
        );
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> Observed(
        TripsHttpFixture fixture,
        TripOptionEffectsProbe probe,
        string label,
        HttpMethod method,
        string path,
        HttpContent? content = null,
        string? expectedPort = null
    )
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add(TripOptionEffectsProbe.Header, label);
        var response = await fixture.Client.SendAsync(request, Ct);
        var completed = await probe.Completion(label, Ct);
        completed.Method.ShouldBe(method.Method);
        completed.Path.ShouldBe(path);
        var counts = probe.Counts(label);
        if (expectedPort is null)
            counts.ShouldBeEmpty();
        else
            counts.ShouldHaveSingleItem().ShouldBe(new KeyValuePair<string, int>(expectedPort, 1));
        probe.Counts("").ShouldBeEmpty();
        probe.Counts("outside-request").ShouldBeEmpty();
        return response;
    }

    [Fact]
    public async Task Negotiated_result_saves_once_and_reopens_when_generation_and_writes_are_off()
    {
        using var ring = new TripsTestRing();
        var store = new FixtureStore();
        using var replayKeys = new TripsProtectionProvider(
            Options.Create(ring.Settings),
            TimeProvider.System
        );
        var probe = new TripOptionEffectsProbe();
        await using var active = await Start(store, ring.Provider, probe);
        using var raw = await active.Client.PostAsync(Preview, Body(Brief), Ct);
        using var old = await Json(raw);
        old.RootElement.TryGetProperty("selection", out _).ShouldBeFalse();
        using var res = await Select(active, Brief);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = await Json(res);
        JsonElement
            .DeepEquals(json.RootElement.GetProperty("preview"), old.RootElement)
            .ShouldBeTrue();
        var selection = json.RootElement.GetProperty("selection");
        var token = selection.GetProperty("selectionToken").GetString()!;
        var id = selection.GetProperty("tripId").GetGuid();
        store.Calls.ShouldBe(0);
        store.Records.ShouldBeEmpty();
        foreach (
            var (label, title, status) in new[]
            {
                ("first-save", "", HttpStatusCode.Created),
                ("same-selection", "Another name", HttpStatusCode.OK),
            }
        )
        {
            using var response = await Observed(
                active,
                probe,
                label,
                HttpMethod.Post,
                Save,
                JsonContent.Create(new { selectionToken = token, title })
            );
            response.StatusCode.ShouldBe(status);
            response.Headers.Location!.OriginalString.ShouldBe("/api/trips/" + id);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            using var location = await Json(response);
            location.RootElement.GetProperty("tripId").GetGuid().ShouldBe(id);
        }
        store.Records.Count.ShouldBe(1);
        var row = store.Records[id].Plan;
        row.Revision.ShouldBe(1);
        JsonDocument
            .Parse(row.ProtectedPayload)
            .RootElement.GetProperty("title")
            .GetString()
            .ShouldNotBeNull()
            .ShouldContain("Санкт-Петербург");
        string path = $"/api/trips/{id}/saved-option";
        var readProbe = new TripOptionEffectsProbe();
        await using var reopened = await Start(store, replayKeys, readProbe, enabled: false);
        using var closed = await Observed(
            reopened,
            readProbe,
            "disabled-save",
            HttpMethod.Post,
            Save,
            JsonContent.Create(new { selectionToken = token })
        );
        closed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var read = await Observed(reopened, readProbe, "history", HttpMethod.Get, path);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var saved = await Json(read);
        var history = saved.RootElement.GetProperty("savedOption");
        history.GetProperty("version").GetInt32().ShouldBe(1);
        JsonElement.DeepEquals(history.GetProperty("preview"), old.RootElement).ShouldBeTrue();
        history.GetProperty("brief").GetProperty("adults").GetInt32().ShouldBe(2);
        store.Manual = id = Guid.NewGuid();
        using var manual = await Observed(
            reopened,
            readProbe,
            "manual",
            HttpMethod.Get,
            $"/api/trips/{id}/saved-option"
        );
        using var manualJson = await Json(manual);
        manualJson.RootElement.GetProperty("savedOption").ValueKind.ShouldBe(JsonValueKind.Null);
        reopened.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        reopened.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        using var foreign = await Observed(
            reopened,
            readProbe,
            "foreign-history",
            HttpMethod.Get,
            path
        );
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var missing = await Observed(
            reopened,
            readProbe,
            "missing-history",
            HttpMethod.Get,
            $"/api/trips/{Guid.NewGuid()}/saved-option"
        );
        missing.StatusCode.ShouldBe(foreign.StatusCode);
        using var missingProblem = await Json(missing);
        using var foreignProblem = await Json(foreign);
        missingProblem.RootElement.GetProperty("code").GetString().ShouldBe("Trips.NotFound");
        foreignProblem.RootElement.GetProperty("code").GetString().ShouldBe("Trips.NotFound");
        var captured = string.Join("\n", active.Logs.Values.Concat(reopened.Logs.Values));
        captured.ShouldNotContain(token);
    }

    [Fact]
    public async Task Wrong_owner_tampered_unknown_fields_and_bounded_bodies_are_rejected()
    {
        using var ring = new TripsTestRing();
        var store = new FixtureStore();
        var probe = new TripOptionEffectsProbe();
        await using var f = await Start(store, ring.Provider, probe);
        using var result = await Select(f, Brief);
        using var json = await Json(result);
        var token = json
            .RootElement.GetProperty("selection")
            .GetProperty("selectionToken")
            .GetString()!;
        var id = json.RootElement.GetProperty("selection").GetProperty("tripId").GetGuid();
        Task<HttpResponseMessage> Post(string label, HttpContent body) =>
            Observed(f, probe, label, HttpMethod.Post, Save, body);
        var badCase = 0;
        foreach (
            var bad in new[]
            {
                """{"selectionToken":"bad","prices":[]}""",
                """{"selectionToken":"bad","selectionToken":"bad"}""",
                """{"selectionToken":null}""",
                """{}""",
            }
        )
        {
            using var failed = await Post($"invalid-{++badCase}", Body(bad));
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        using var oversize = await Post("oversize", Body(new string(' ', 131073)));
        oversize.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var streamed = new StreamContent(new NonSeekableBytes(new byte[131073]));
        streamed.Headers.ContentType = new("application/json");
        using var streamedResponse = await Post("streamed-oversize", streamed);
        streamedResponse.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var atBoundary = await Post(
            "at-boundary",
            Body("""{"selectionToken":"bad"}""".PadRight(131072))
        );
        atBoundary.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        foreach (
            var (length, expected) in new[]
            {
                (65536, HttpStatusCode.Conflict),
                (65537, HttpStatusCode.BadRequest),
            }
        )
        {
            using var bounded = await Post(
                $"token-{length}",
                JsonContent.Create(new { selectionToken = new string('A', length) })
            );
            bounded.StatusCode.ShouldBe(expected);
        }
        using var tamper = await Post(
            "tampered",
            JsonContent.Create(new { selectionToken = token + "X" })
        );
        tamper.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        f.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        f.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        using var foreign = await Post(
            "foreign",
            JsonContent.Create(new { selectionToken = token })
        );
        foreign.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        store.Calls.ShouldBe(0);
        f.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        f.Client.DefaultRequestHeaders.Add("X-Trips-Owner", TripsHttpFixture.Owner.ToString());
        using var created = await Post(
            "named",
            JsonContent.Create(new { selectionToken = token, title = "Осенний отпуск" })
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonDocument
            .Parse(store.Records[id].Plan.ProtectedPayload)
            .RootElement.GetProperty("title")
            .GetString()
            .ShouldBe("Осенний отпуск");
        using var otherKeys = new TripsTestRing();
        var outageProbe = new TripOptionEffectsProbe();
        await using var outage = await Start(
            store,
            otherKeys.Provider,
            outageProbe,
            enabled: false
        );
        var path = $"/api/trips/{id}/saved-option";
        using var unreadable = await Observed(
            outage,
            outageProbe,
            "wrong-key",
            HttpMethod.Get,
            path
        );
        unreadable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        store.Records[id] = store.Records[id] with { ProtectedOption = "damaged" };
        using var corrupt = await Observed(f, probe, "corrupt", HttpMethod.Get, path);
        corrupt.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Preview_only_and_ineligible_results_remain_usable_without_writes()
    {
        using var ring = new TripsTestRing();
        var store = new FixtureStore();
        await using var f = await Start(store, ring.Provider, new TripOptionEffectsProbe());
        foreach (
            var body in new[]
            {
                Brief.Replace("\"adults\":2", "\"adults\":3"),
                Brief.Replace("10-28", "10-29"),
            }
        )
        {
            using var r = await Select(f, body);
            using var j = await Json(r);
            j.RootElement.GetProperty("selection").ValueKind.ShouldBe(JsonValueKind.Null);
            j.RootElement.GetProperty("preview")
                .GetProperty("outcome")
                .GetString()
                .ShouldNotBe("option");
        }
        using var invalid = new HttpRequestMessage(HttpMethod.Post, Preview)
        {
            Content = Body(Brief),
        };
        invalid.Headers.Add(Header, "9");
        (await f.Client.SendAsync(invalid, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await using var noKeys = await TripsHttpFixture.Start(
            itinerary: true,
            environment: "Testing",
            optionPreview: true,
            optionProfile: "spb-option",
            optionCatalogue: "B",
            saveOptions: true,
            ready: false
        );
        using var fallback = await Select(noKeys, Brief);
        using var fallbackJson = await Json(fallback);
        fallbackJson.RootElement.GetProperty("selection").ValueKind.ShouldBe(JsonValueKind.Null);
        fallbackJson
            .RootElement.GetProperty("preview")
            .GetProperty("outcome")
            .GetString()
            .ShouldBe("option");
        store.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Changed_catalogue_refuses_first_write_but_replays_an_already_committed_selection()
    {
        using var ring = new TripsTestRing();
        var store = new FixtureStore();
        var bProbe = new TripOptionEffectsProbe();
        await using var b = await Start(store, ring.Provider, bProbe);
        using var selected = await Select(b, Brief);
        using var json = await Json(selected);
        var token = json
            .RootElement.GetProperty("selection")
            .GetProperty("selectionToken")
            .GetString();
        var aProbe = new TripOptionEffectsProbe();
        await using var a = await Start(store, ring.Provider, aProbe, catalogue: "A");
        using var rejected = await Observed(
            a,
            aProbe,
            "catalogue-changed",
            HttpMethod.Post,
            Save,
            JsonContent.Create(new { selectionToken = token })
        );
        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        store.Records.ShouldBeEmpty();
        using var saved = await Observed(
            b,
            bProbe,
            "catalogue-b",
            HttpMethod.Post,
            Save,
            JsonContent.Create(new { selectionToken = token })
        );
        saved.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var replay = await Observed(
            a,
            aProbe,
            "catalogue-replay",
            HttpMethod.Post,
            Save,
            JsonContent.Create(new { selectionToken = token })
        );
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.Location.ShouldBe(saved.Headers.Location);
        store.Records.Count.ShouldBe(1);
    }

    private static Task<TripsHttpFixture> Start(
        FixtureStore store,
        TripsProtectionProvider keys,
        TripOptionEffectsProbe probe,
        bool enabled = true,
        string catalogue = "B"
    ) =>
        TripsHttpFixture.Start(
            itinerary: enabled,
            environment: "Testing",
            optionPreview: enabled,
            optionProfile: "spb-option",
            optionCatalogue: catalogue,
            saveOptions: enabled,
            configure: s =>
            {
                s.AddSingleton<ISavedOptionProtection>(new SavedOptionProtection(keys));
                s.AddSingleton<ISavedOptionStore>(store);
                s.AddScoped<SavedOptionService>();
                s.AddSingleton(TimeProvider.System);
            },
            configureAfterWolverine: probe.Register
        );

    private static async Task<HttpResponseMessage> Select(TripsHttpFixture f, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Preview)
        {
            Content = Body(body),
        };
        request.Headers.Add(Header, "1");
        return await f.Client.SendAsync(request, Ct);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));

    private static StringContent Body(string text) => new(text, Encoding.UTF8, "application/json");

    private sealed class NonSeekableBytes(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    // Storage transaction and admission races are exercised separately against real PostgreSQL.
    private sealed class FixtureStore : ISavedOptionStore
    {
        internal readonly Dictionary<Guid, NewSavedOption> Records = [];
        internal Guid Manual;
        internal int Calls;
        internal Func<CancellationToken, Task>? ExerciseBlockedPort;

        public async Task<ErrorOr<SavedOptionWrite>> TrySaveAsync(
            OwnerIdentity owner,
            Guid id,
            DateTimeOffset expires,
            Func<CancellationToken, Task<ErrorOr<NewSavedOption>>> prepare,
            CancellationToken ct
        )
        {
            Calls++;
            if (ExerciseBlockedPort is not null)
                await ExerciseBlockedPort(ct);
            if (Records.ContainsKey(id))
                return new SavedOptionWrite(id, false);
            var fresh = await prepare(ct);
            if (fresh.IsError)
                return fresh.Errors;
            Records.Add(id, fresh.Value);
            return new SavedOptionWrite(id, true);
        }

        public async Task<ErrorOr<SavedOptionEnvelope>> LoadAsync(
            OwnerIdentity owner,
            Guid id,
            CancellationToken ct
        )
        {
            if (ExerciseBlockedPort is not null)
                await ExerciseBlockedPort(ct);
            ErrorOr<SavedOptionEnvelope> value =
                id == Manual && owner.Subject == TripsHttpFixture.Owner
                    ? new SavedOptionEnvelope(null)
                : Records.TryGetValue(id, out var r) && r.Plan.Owner == owner
                    ? new SavedOptionEnvelope(r.ProtectedOption)
                : TripErrors.NotFound;
            return value;
        }
    }
}
