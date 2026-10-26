using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

// Frozen structural IDs. The twelfth, N05, runs in the actual Angular destination-epoch test.
public sealed class TripsSaintPetersburgStructuralTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("S01")]
    [InlineData("S02")]
    [InlineData("N03")]
    [InlineData("N04")]
    [InlineData("N06")]
    public async Task Structural_source_refusal_is_unavailable_without_Moscow_fallback(string id)
    {
        using var package = new CuratedSaintPetersburgFactsTests.Package();
        if (id == "S01")
            package.Clock.Now = DateTimeOffset.Parse("2026-11-24T04:08:52Z");
        if (id == "S02")
            package.Clock.Now = DateTimeOffset.Parse("2026-10-26T10:00:00Z");
        if (id == "N04")
            File.AppendAllText(
                Path.Combine(package.Directory, "saint-petersburg-v1.manifest.json"),
                " "
            );
        var source = new CuratedItineraryFacts(
            package.Directory,
            package.Clock,
            () => id == "N06",
            id == "N03"
                ? CuratedItineraryRelease.ReviewedV1
                : CuratedItineraryRelease.ReviewedSaintPetersburgV1
        );
        (await source.ReadAsync(Ct)).IsError.ShouldBeTrue();
        await using var f = await TripsHttpFixture.Start(
            ready: false,
            itinerary: true,
            configure: s =>
            {
                s.AddSingleton<TimeProvider>(package.Clock);
                s.AddSingleton<IItineraryFactSource>(source);
            }
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/trips/itinerary-availability?destinationId=saint-petersburg"
        );
        request.Headers.Add("X-Trips-Itinerary-Version", "2");
        using var result = await f.Client.SendAsync(request, Ct);
        result.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        f.Protection.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("S03", "2026-11-22", 200, "unsupported")]
    [InlineData("S04", "2026-10-20", 200, "unsupported")]
    [InlineData("N01", "2026-10-22", 404, null)]
    [InlineData("N02", "2026-10-22", 412, null)]
    public async Task Structural_scope_owner_and_revision_precede_source(
        string id,
        string date,
        int status,
        string? outcome
    )
    {
        var source = new FictionalItinerarySource
        {
            Facts = FictionalItineraryFacts.Input("saint-petersburg", true).Build().Value,
        };
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var created = await f.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(Create(date)),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        if (id == "N01")
        {
            f.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
            f.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        }
        using var response = await Candidate(
            f,
            created.Headers.Location!,
            id == "N02" ? "\"2\"" : "\"1\""
        );
        ((int)response.StatusCode).ShouldBe(status);
        source.Calls.ShouldBe(0);
        if (outcome is not null)
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
            body.RootElement.GetProperty("outcome").GetString().ShouldBe(outcome);
        }
    }

    [Fact]
    public async Task S05_same_day_expiry_before_local_day_end_produces_empty_without_invented_facts()
    {
        var input = FictionalItineraryFacts.Input("saint-petersburg", true);
        var until = FictionalItineraryFacts.Now.AddHours(1);
        var evidence = input
            .Evidence.Select(e =>
                e.Kind == ItineraryFactKind.IdentityLocation
                    ? (
                        FictionalItineraryFacts.EvidenceParameters(e.Id, e.Kind, e.ScopeId) with
                        {
                            ValidUntil = until,
                        }
                    )
                        .Build()
                        .Value
                    : e
            )
            .ToArray();
        var built = (input with { Evidence = evidence, ReleaseValidUntil = until }).Build();
        built.IsError.ShouldBeFalse();
        var source = new FictionalItinerarySource { Facts = built.Value };
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var created = await f.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(Create("2026-10-21")),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var response = await Candidate(f, created.Headers.Location!);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        body.RootElement.GetProperty("outcome").GetString().ShouldBe("empty");
        body.RootElement.GetProperty("days")[0].GetProperty("blocks").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task S06_disconnected_eligible_graph_produces_partial_without_invented_adjacency()
    {
        var input = FictionalItineraryFacts.Input("saint-petersburg", true);
        // p0 and p9 have no directed connection in the fixed graph; all other
        // POIs are explicitly closed by fictional access evidence.
        var closed = input.Pois.Where(p => p.Id is not ("p0" or "p9")).ToArray();
        var evidence = input
            .Evidence.Concat(
                closed.Select(p =>
                    FictionalItineraryFacts
                        .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                        .Build()
                        .Value
                )
            )
            .ToArray();
        var pois = input
            .Pois.Select(p =>
                p.Id is "p0" or "p9"
                    ? p
                    : FictionalItineraryFacts.Poi(
                        p.Id,
                        tags: p.Tags.ToArray(),
                        access: ItineraryAccess.Create(p.Id, true, "access-" + p.Id, [], []).Value,
                        latitude: p.Latitude,
                        longitude: p.Longitude
                    )
            )
            .ToArray();
        var built = (input with { Evidence = evidence, Pois = pois }).Build();
        built.IsError.ShouldBeFalse();
        var source = new FictionalItinerarySource { Facts = built.Value };
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var created = await f.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(Create("2026-10-22")),
            Ct
        );
        using var response = await Candidate(f, created.Headers.Location!);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        body.RootElement.GetProperty("coverage").GetString().ShouldBe("partial");
        body.RootElement.GetProperty("days")[0].GetProperty("blocks").GetArrayLength().ShouldBe(1);
        body.RootElement.GetProperty("days")[0].GetProperty("edges").GetArrayLength().ShouldBe(0);
    }

    private static string Create(string date) =>
        TripsItineraryHttpTests
            .CreateJson.Replace(
                "\"destinationId\":\"moscow\"",
                "\"destinationId\":\"saint-petersburg\""
            )
            .Replace("\"originId\":\"saint-petersburg\"", "\"originId\":\"moscow\"")
            .Replace("2026-10-22", date);

    private static Task<HttpResponseMessage> Candidate(
        TripsHttpFixture f,
        Uri path,
        string revision = "\"1\""
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path + "/itinerary-candidate")
        {
            Content = TripsHttpTests.Body("{}"),
        };
        request.Headers.Add("X-Trips-Itinerary-Version", "2");
        request.Headers.Add("If-Match", revision);
        return f.Client.SendAsync(request, Ct);
    }
}
