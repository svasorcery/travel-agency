using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Xunit;

namespace Travel.Host.Tests.Integration.NlSearch;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class FlightSearchPreviewTransportTests
{
    private const string Preview = "/api/flights/search/nl/preview";
    private const string Search = "/api/flights/search";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Real_host_and_ai_support_review_manual_fallback_and_the_existing_searches()
    {
        await using var fixture = await NlSearchTransportFixture.StartAsync(
            Ct,
            previewEnabled: true
        );
        using var client = fixture.CreatePreviewHttpClient();
        await fixture.InstallBriefDurableAuditAsync(Ct);
        const string marker = "FICTIONAL_PREVIEW_PRIVATE_97";
        var inputs = new[]
        {
            new
            {
                Query = "Из Москвы в Сочи 12 декабря 2026, обратно 15 декабря 2026, двое взрослых",
                Origin = "MOW",
                Departure = "2026-12-12",
                Return = (string?)"2026-12-16",
                Status = HttpStatusCode.OK,
            },
            new
            {
                Query = "Хочу в Сочи на выходные, нас двое",
                Origin = "LED",
                Departure = "2026-11-28",
                Return = (string?)"2026-11-30",
                Status = HttpStatusCode.OK,
            },
            new
            {
                Query = marker,
                Origin = "LED",
                Departure = "2026-12-12",
                Return = (string?)null,
                Status = HttpStatusCode.ServiceUnavailable,
            },
        };
        var confirmed = 0;
        foreach (var input in inputs)
        {
            using var preview = await client.PostAsJsonAsync(
                Preview,
                new { query = input.Query, referenceDate = "2026-11-23" },
                Ct
            );
            var json = await preview.Content.ReadAsStringAsync(Ct);
            preview.StatusCode.ShouldBe(input.Status, json);
            preview.Headers.CacheControl?.NoStore.ShouldBe(true);
            json.ShouldNotContain(marker);
            fixture.PreviewTestSearches.ShouldBe(confirmed);
            fixture.PreviewTestSupplierCalls.ShouldBe(confirmed);
            fixture.PreviewModelCalls.ShouldBe(confirmed + 1);
            fixture.LegacyModelCalls.ShouldBe(0);

            if (preview.IsSuccessStatusCode)
            {
                using var draft = JsonDocument.Parse(json);
                draft
                    .RootElement.GetProperty("destination")
                    .GetProperty("code")
                    .GetString()
                    .ShouldBe("AER");
                draft.RootElement.GetProperty("passengerCount").GetInt32().ShouldBe(2);
                draft
                    .RootElement.GetProperty("departureDate")
                    .GetProperty("value")
                    .GetString()
                    .ShouldBe(input.Departure);
                var origin = draft.RootElement.GetProperty("origin");
                if (confirmed == 0)
                    origin.GetProperty("code").GetString().ShouldBe("MOW");
                else
                    origin.ValueKind.ShouldBe(JsonValueKind.Null);
            }

            using var search = await client.PostAsJsonAsync(
                Search,
                new
                {
                    origin = input.Origin,
                    destination = "AER",
                    departureDate = input.Departure,
                    returnDate = input.Return,
                    passengerCount = 2,
                    cabinClass = "economy",
                },
                Ct
            );
            search.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                await search.Content.ReadAsStringAsync(Ct)
            );
            confirmed++;
            fixture.PreviewTestSearches.ShouldBe(confirmed);
            fixture.PreviewTestSupplierCalls.ShouldBe(confirmed);
            var criteria = fixture.LastPreviewTestCriteria.ShouldNotBeNull();
            criteria.Origin.Value.ShouldBe(input.Origin);
            criteria.Destination.Value.ShouldBe("AER");
            criteria.PassengerCount.ShouldBe(2);
            criteria.DepartureDate.ShouldBe(DateOnly.Parse(input.Departure));
            criteria.ReturnDate.ShouldBe(
                input.Return is null ? null : DateOnly.Parse(input.Return)
            );
        }

        using var legacy = await client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query = "Fly from Moscow to Kazan on September 20" },
            Ct
        );
        var legacyBody = await legacy.Content.ReadAsStringAsync(Ct);
        legacy.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            legacyBody
                + $"\nFictional v1 calls: {fixture.LegacyModelCalls}, preview: {fixture.PreviewModelCalls}\n"
                + string.Join(
                    "\n",
                    fixture
                        .PreviewLogs.Where(message =>
                            message.Contains("NlSearch", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("NL-search", StringComparison.OrdinalIgnoreCase)
                        )
                        .TakeLast(12)
                )
        );
        fixture.LegacyModelCalls.ShouldBe(1);
        fixture.PreviewModelCalls.ShouldBe(3);
        fixture.PreviewTestSearches.ShouldBe(4);
        fixture.PreviewTestSupplierCalls.ShouldBe(4);

        fixture.DisablePreviewOnHost();
        using var disabled = await client.PostAsJsonAsync(
            Preview,
            new { query = "В Сочи", referenceDate = "2026-11-23" },
            Ct
        );
        disabled.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.PreviewModelCalls.ShouldBe(3);
        using var manual = await client.PostAsJsonAsync(
            Search,
            new
            {
                origin = "LED",
                destination = "AER",
                departureDate = "2026-12-12",
                passengerCount = 2,
                cabinClass = "economy",
            },
            Ct
        );
        manual.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.PreviewTestSearches.ShouldBe(5);
        using var multiLeg = await client.PostAsJsonAsync(
            Search + "/v2",
            new
            {
                legs = new[]
                {
                    new
                    {
                        origin = "LED",
                        destination = "AER",
                        departureDate = "2026-12-12",
                    },
                    new
                    {
                        origin = "AER",
                        destination = "DME",
                        departureDate = "2026-12-15",
                    },
                },
                passengerCount = 2,
                cabinClass = "economy",
            },
            Ct
        );
        multiLeg.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            await multiLeg.Content.ReadAsStringAsync(Ct)
        );
        fixture.LastPreviewTestCriteria!.RouteMode.ShouldBe(SearchRouteMode.ExplicitAirportLegs);
        fixture.PreviewTestSearches.ShouldBe(6);
        fixture.PreviewTestSupplierCalls.ShouldBe(6);

        await fixture.StopAllAiAsync(Ct);
        await fixture.WaitForAiListenerMembershipAsync(0, Ct);
        using var legacyUnavailable = await client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query = "Fly from Moscow to Kazan on September 20" },
            Ct
        );
        legacyUnavailable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await legacyUnavailable.Content.ReadAsStringAsync(Ct)).ShouldContain(
            "Flights.NlSearchUnparseable"
        );
        fixture.PreviewTestSearches.ShouldBe(6);
        fixture.PreviewTestSupplierCalls.ShouldBe(6);
        fixture.PreviewLogs.ShouldNotContain(message =>
            message.Contains(marker, StringComparison.Ordinal)
        );
        await fixture.AssertBriefDurablePrivacyAsync(marker, Ct);
    }
}
