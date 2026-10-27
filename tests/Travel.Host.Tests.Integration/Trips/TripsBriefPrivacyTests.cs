using System.Net;
using Shouldly;
using Travel.Host.Tests.Integration.NlSearch;
using Xunit;
using static Travel.Host.Tests.Integration.Trips.TripsBriefHostTests;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripsBriefPrivacyTests
{
    [Fact(Timeout = 180_000)]
    public async Task Real_programs_keep_private_text_and_candidate_out_of_logs_traces_and_all_durable_tables_on_failures()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await NlSearchTransportFixture.StartAsync(ct, briefEnabled: true);
        using var client = fixture.CreateBriefHttpClient();
        await fixture.WaitForBriefMembershipAsync(2, ct);
        await fixture.AssertBriefProductionCompositionAsync(ct);
        await fixture.InstallBriefDurableAuditAsync(ct);
        var marker = "M3_PRIVATE_" + Guid.NewGuid().ToString("N");
        foreach (
            var (mode, status) in new[]
            {
                ("Ready", HttpStatusCode.OK),
                ("Exception", HttpStatusCode.ServiceUnavailable),
                ("InvalidOutput", HttpStatusCode.BadGateway),
                ("WrongModel", HttpStatusCode.ServiceUnavailable),
                ("UsageUnknown", HttpStatusCode.ServiceUnavailable),
            }
        )
        {
            fixture.SetBriefMode(mode, marker);
            var id = Guid.NewGuid();
            var calls = fixture.BriefCalls;
            using var response = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(id, "Москва. " + marker)
            );
            response.StatusCode.ShouldBe(status);
            AssertNoStore(response);
            (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain(marker);
            fixture.BriefCalls.ShouldBe(calls + 1, "one provider call with no automatic retry");
            var row = (await fixture.ReadBriefLedgerAsync(id, ct)).ShouldHaveSingleItem();
            row.UserId.ShouldBeNull();
            if (mode is "Exception" or "WrongModel" or "UsageUnknown")
                row.CostUsd.ShouldBeNull();
            await fixture.AssertBriefDurablePrivacyAsync(marker, ct);
        }
        // Request-reader failures must also remain private and make no bus/provider call.
        var beforeInvalid = fixture.BriefCalls;
        using (
            var invalid = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(Guid.NewGuid(), "Москва. " + marker)
                    .Replace("\"text\":", "\"ownerId\":\"foreign\",\"text\":")
            )
        )
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            AssertNoStore(invalid);
            (await invalid.Content.ReadAsStringAsync(ct)).ShouldNotContain(marker);
        }
        using (
            var large = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(Guid.NewGuid(), marker + new string('x', 8192))
            )
        )
        {
            large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            AssertNoStore(large);
            (await large.Content.ReadAsStringAsync(ct)).ShouldNotContain(marker);
        }
        fixture.BriefCalls.ShouldBe(beforeInvalid);
        fixture.SetBriefMode("Ready", marker);
        var beforeOutage = fixture.BriefCalls;
        await fixture.SetBriefLedgerOutageAsync(true, ct);
        try
        {
            using var unavailable = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(Guid.NewGuid(), "Москва. " + marker)
            );
            unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            AssertNoStore(unavailable);
            (await unavailable.Content.ReadAsStringAsync(ct)).ShouldNotContain(marker);
            fixture.BriefCalls.ShouldBe(
                beforeOutage,
                "actual production EF admission failed before model dispatch"
            );
        }
        finally
        {
            await fixture.SetBriefLedgerOutageAsync(false, ct);
        }
        await Task.Delay(200, ct);
        fixture.BriefCalls.ShouldBe(beforeOutage);
        var tables = await fixture.AssertBriefDurablePrivacyAsync(marker, ct);
        tables.ShouldContain("ai.cost_ledger");
        tables.Any(t => t.Contains("dead", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
        fixture.BriefLogs.Length.ShouldBeGreaterThan(
            0,
            "capture must exercise actual log/trace emitters including failures"
        );
        fixture
            .BriefLogs.Any(log => log.Contains(marker, StringComparison.Ordinal))
            .ShouldBeFalse(
                "capture includes formatted state, scopes, full exceptions, activities and events"
            );
        fixture
            .BriefLogs.Any(log => log.Contains("trips_m3_fixture_outage", StringComparison.Ordinal))
            .ShouldBeTrue(
                "actual EF outage reaches the captured diagnostics, without private text"
            );
    }
}
