using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

// Real packages are read only for availability here. Candidate generation below must use
// explicit FICTIONAL facts until the independent real-Q run binding has been frozen.
public sealed class TripsItineraryV2HttpTests
{
    [Theory]
    [InlineData("moscow")]
    [InlineData("saint-petersburg")]
    public async Task V2_candidate_uses_owner_saved_city_and_explicit_version_without_writing(
        string city
    )
    {
        var source = new FictionalItinerarySource
        {
            Facts = FictionalItineraryFacts.Input(city, v2: true).Build().Value,
        };
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var created = await f.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(
                TripsItineraryHttpTests.CreateJson.Replace(
                    "\"destinationId\":\"moscow\"",
                    "\"destinationId\":\"" + city + "\""
                )
            ),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            created.Headers.Location + "/itinerary-candidate"
        )
        {
            Content = TripsHttpTests.Body("{}"),
        };
        request.Headers.Add(Header, "2");
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var response = await f.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        body.RootElement.GetProperty("candidateVersion").GetInt32().ShouldBe(2);
        body.RootElement.GetProperty("destinationId").GetString().ShouldBe(city);
        body.RootElement.GetProperty("derivedDataOffer")
            .GetString()
            .ShouldBe($"/trips-data/{city}-outdoor/1/{city}-v1.manifest.json");
        source.Calls.ShouldBe(1);
        using var saved = await f.Client.GetAsync(created.Headers.Location, Ct);
        using var detail = JsonDocument.Parse(await saved.Content.ReadAsByteArrayAsync(Ct));
        detail.RootElement.GetProperty("revision").GetString().ShouldBe("1");
        detail
            .RootElement.GetProperty("days")[0]
            .GetProperty("blocks")
            .GetArrayLength()
            .ShouldBe(0);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Header = "X-Trips-Itinerary-Version";
    private const string Availability = "/api/trips/itinerary-availability";

    [Theory]
    [InlineData("moscow", "moscow-outdoor")]
    [InlineData("saint-petersburg", "saint-petersburg-outdoor")]
    public async Task V2_availability_selects_exact_city_without_private_plan_readiness(
        string city,
        string dataset
    )
    {
        var clock = new ItineraryHttpClock { Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z") };
        await using var f = await TripsHttpFixture.Start(
            ready: false,
            itinerary: true,
            configure: s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.RemoveAll<IItineraryFactSource>();
                CuratedItineraryFacts Source(CuratedItineraryRelease release) =>
                    new(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "Trips",
                            "Itineraries",
                            release.DatasetId,
                            release.Version
                        ),
                        clock,
                        expectedRelease: release
                    );
                s.AddSingleton<IItineraryFactSource>(
                    new DestinationItineraryFacts(
                        Source(CuratedItineraryRelease.ReviewedV1),
                        Source(CuratedItineraryRelease.ReviewedSaintPetersburgV1)
                    )
                );
            }
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Availability + "?destinationId=" + city
        );
        request.Headers.Add(Header, "2");
        using var response = await f.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        body.Length.ShouldBeLessThanOrEqualTo(2048);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("candidateVersion").GetInt32().ShouldBe(2);
        json.RootElement.GetProperty("destinationId").GetString().ShouldBe(city);
        json.RootElement.GetProperty("datasetId").GetString().ShouldBe(dataset);
        f.Protection.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Catalogue_only_city_is_explicit_unsupported_and_source_free()
    {
        var source = new FictionalItinerarySource();
        await using var f = await TripsHttpFixture.Start(
            ready: false,
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Availability + "?destinationId=kaliningrad"
        );
        request.Headers.Add(Header, "2");
        using var response = await f.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        body.RootElement.EnumerateObject().Count().ShouldBe(4);
        body.RootElement.GetProperty("status").GetString().ShouldBe("unsupported");
        body.RootElement.TryGetProperty("datasetHash", out _).ShouldBeFalse();
        source.Calls.ShouldBe(0);
        f.Protection.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("2", "")]
    [InlineData("1", "")]
    [InlineData("3", "")]
    [InlineData("2,2", "?destinationId=moscow")]
    [InlineData("2", "?destinationId=moscow&destinationId=saint-petersburg")]
    [InlineData("2", "?destinationId=Moscow")]
    [InlineData("2", "?destinationId=unknown")]
    [InlineData("2", "?destinationId=moscow&extra=1")]
    [InlineData("2", "?DestinationId=moscow")]
    public async Task Protocol_and_destination_are_strict_and_do_not_load_source(
        string version,
        string query
    )
    {
        var source = new FictionalItinerarySource();
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, Availability + query);
        request.Headers.TryAddWithoutValidation(Header, version);
        using var response = await f.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_version_header_is_rejected_without_source()
    {
        var source = new FictionalItinerarySource();
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Availability + "?destinationId=moscow"
        );
        request.Headers.TryAddWithoutValidation(Header, new[] { "2", "2" });
        using var response = await f.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Present_empty_version_is_not_treated_as_an_absent_legacy_header()
    {
        var source = new FictionalItinerarySource();
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        // HttpClient omits an empty custom header; inject it at the actual server boundary.
        var result = await f
            .App.GetTestServer()
            .SendAsync(
                context =>
                {
                    context.Request.Method = "GET";
                    context.Request.Path = Availability;
                    context.Request.Headers["X-Trips-Owner"] = TripsHttpFixture.Owner.ToString();
                    context.Request.Headers[Header] = "";
                },
                Ct
            );
        result.Response.StatusCode.ShouldBe(400);
        source.Calls.ShouldBe(0);
    }
}
