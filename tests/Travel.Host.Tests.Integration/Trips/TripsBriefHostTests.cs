using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Host.Tests.Integration.NlSearch;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripsBriefHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = 180_000)]
    public async Task Actual_Host_capability_and_candidate_use_core_nats_without_M1_store()
    {
        await using var fixture = await NlSearchTransportFixture.StartAsync(Ct, briefEnabled: true);
        using var client = fixture.CreateBriefHttpClient();
        var membership = await fixture.WaitForBriefMembershipAsync(2, Ct);
        membership.Subject.ShouldBe(TripsBriefMessageIdentity.Subject);
        membership.QueueGroup.ShouldBe(TripsBriefMessageIdentity.QueueGroup);
        membership.DistinctConnectionCount.ShouldBe(2);
        await fixture.AssertBriefProductionCompositionAsync(Ct);
        await fixture.InstallBriefDurableAuditAsync(Ct);
        foreach (
            var path in new[]
            {
                "/api/trips/brief-extraction-capability",
                "/API/TRIPS/BRIEF-EXTRACTION-CAPABILITY/",
            }
        )
        {
            using var response = await Send(client, HttpMethod.Get, path);
            AssertNoStore(response);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
            bytes.Length.ShouldBeLessThanOrEqualTo(2048);
            using var capability = JsonDocument.Parse(bytes);
            var value = capability.RootElement;
            value.GetProperty("status").GetString().ShouldBe("configured");
            value.GetProperty("processorId").GetString().ShouldBe("anthropic");
            value.GetProperty("modelId").GetString().ShouldBe(NlSearchTransportFixture.BriefModel);
            value
                .GetProperty("processingPolicyVersion")
                .GetString()
                .ShouldBe(NlSearchTransportFixture.BriefPolicy);
            value.EnumerateObject().Count().ShouldBe(5);
        }
        fixture.BriefCalls.ShouldBe(0);
        using (
            var anonymous = await Send(
                client,
                HttpMethod.Get,
                "/api/trips/brief-extraction-capability",
                authenticated: false
            )
        )
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            AssertNoStore(anonymous);
        }
        using (
            var foreign = await Send(
                client,
                HttpMethod.Get,
                "/api/trips/brief-extraction-capability",
                untrusted: true
            )
        )
        {
            foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            AssertNoStore(foreign);
        }
        var requestId = Guid.NewGuid();
        var observed = fixture.ObserveBriefEnvelopeAsync(Ct);
        using (
            var ready = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(requestId, "Москва.")
            )
        )
        {
            ready.StatusCode.ShouldBe(HttpStatusCode.OK);
            AssertNoStore(ready);
            var bytes = await ready.Content.ReadAsByteArrayAsync(Ct);
            bytes.Length.ShouldBeLessThanOrEqualTo(16384);
            using var document = JsonDocument.Parse(bytes);
            var value = document.RootElement;
            value.GetProperty("requestId").GetGuid().ShouldBe(requestId);
            value.GetProperty("candidateVersion").GetInt32().ShouldBe(1);
            value
                .GetProperty("fields")
                .GetProperty("destinationId")
                .GetProperty("value")
                .GetString()
                .ShouldBe("moscow");
            value.TryGetProperty("ownerId", out _).ShouldBeFalse();
            value.TryGetProperty("tripId", out _).ShouldBeFalse();
        }
        var envelope = await observed.WaitAsync(TimeSpan.FromSeconds(3), Ct);
        envelope.Subject.ShouldBe(TripsBriefMessageIdentity.Subject);
        envelope.Members.ShouldBe(
            new[]
            {
                "correlationid",
                "deadline",
                "locale",
                "processingpolicyversion",
                "referenceinstant",
                "referencelocaldate",
                "referencezone",
                "text",
            }
        );
        envelope
            .HeaderNames.Any(name =>
                new[] { "authorization", "owner", "subject", "issuer", "user" }.Any(privateName =>
                    name.Contains(privateName, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ShouldBeFalse();
        TestContext.Current.TestOutputHelper?.WriteLine(
            "M3_TRANSPORT_HEADERS:" + JsonSerializer.Serialize(envelope.HeaderNames)
        );
        fixture.BriefCalls.ShouldBe(1);
        var row = (await fixture.ReadBriefLedgerAsync(requestId, Ct)).ShouldHaveSingleItem();
        row.Outcome.ShouldBe("Ready");
        row.Model.ShouldBe(NlSearchTransportFixture.BriefModel);
        row.UserId.ShouldBeNull();
        foreach (var path in new[] { "/API/TRIPS/BRIEF-CANDIDATE/", "/api/trips/brief-candidate/" })
        {
            using var response = await Send(
                client,
                HttpMethod.Post,
                path,
                Body(Guid.NewGuid(), "Москва.")
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            AssertNoStore(response);
        }
        var calls = fixture.BriefCalls;
        using (
            var stale = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(Guid.NewGuid(), "Москва.", "stale")
            )
        )
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await stale.Content.ReadAsStringAsync(Ct)).ShouldContain(
                "Trips.ProcessingPolicyChanged"
            );
        }
        foreach (
            var body in new[]
            {
                Body(Guid.NewGuid(), "Москва.")
                    .Replace("\"text\":", "\"tripId\":\"foreign\",\"text\":"),
                Body(Guid.NewGuid(), "Москва.")
                    .Replace("\"text\":", "\"ownerId\":\"foreign\",\"text\":"),
            }
        )
        {
            using var response = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                body
            );
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            AssertNoStore(response);
        }
        using (
            var withQuery = await Send(
                client,
                HttpMethod.Get,
                "/api/trips/brief-extraction-capability?text=forbidden"
            )
        )
            withQuery.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var m1 = await Send(client, HttpMethod.Put, "/api/trips/" + Guid.NewGuid(), "{}"))
            m1.StatusCode.ShouldBe((HttpStatusCode)428);
        using (
            var m1Large = await Send(
                client,
                HttpMethod.Post,
                "/api/trips",
                new string('x', 128 * 1024 + 1)
            )
        )
        {
            m1Large.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await m1Large.Content.ReadAsStringAsync(Ct)).ShouldContain("Trips.PlanTooLarge");
        }
        fixture.BriefCalls.ShouldBe(calls);
        using (var openapi = await Send(client, HttpMethod.Get, "/openapi/v1.json"))
        {
            openapi.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await openapi.Content.ReadAsStringAsync(Ct));
            var paths = document.RootElement.GetProperty("paths");
            paths
                .GetProperty("/api/trips/brief-candidate")
                .GetProperty("post")
                .GetProperty("responses")
                .TryGetProperty("200", out _)
                .ShouldBeTrue();
            paths
                .GetProperty("/api/trips/brief-extraction-capability")
                .GetProperty("get")
                .GetProperty("responses")
                .TryGetProperty("200", out _)
                .ShouldBeTrue();
        }
        await fixture.StopAllAiAsync(Ct);
        (await fixture.WaitForBriefMembershipAsync(0, Ct)).SubscriptionCount.ShouldBe(0);
        using (
            var capability = await Send(
                client,
                HttpMethod.Get,
                "/api/trips/brief-extraction-capability"
            )
        )
            capability.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "metadata does not probe the stopped AI responder"
            );
        var notReplayedId = Guid.NewGuid();
        using (
            var missing = await Send(
                client,
                HttpMethod.Post,
                "/api/trips/brief-candidate",
                Body(notReplayedId, "Москва. M3_ABSENT_RESPONDER")
            )
        )
        {
            missing.StatusCode.ShouldBeOneOf(
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.GatewayTimeout
            );
            AssertNoStore(missing);
            var problem = await missing.Content.ReadAsStringAsync(Ct);
            problem.ShouldNotContain("M3_ABSENT_RESPONDER");
            problem.ShouldContain("Trips.Brief");
        }
        fixture.BriefCalls.ShouldBe(calls);
        await fixture.RestartOneBriefAiAsync(Ct);
        await Task.Delay(200, Ct);
        fixture.BriefCalls.ShouldBe(calls, "Core NATS does not replay the no-responder request");
        (await fixture.ReadBriefLedgerAsync(notReplayedId, Ct)).ShouldBeEmpty();
        var tables = await fixture.AssertBriefDurablePrivacyAsync("M3_ABSENT_RESPONDER", Ct);
        tables.Any(n => n.Contains("incoming", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
        tables.Any(n => n.Contains("outgoing", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
        tables.Any(n => n.Contains("dead", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    [Fact(Timeout = 180_000)]
    public async Task Actual_Host_disabled_M3_returns_404_before_auth_and_has_no_M3_listener()
    {
        await using var fixture = await NlSearchTransportFixture.StartAsync(
            Ct,
            briefEnabled: true,
            briefFeatureEnabled: false
        );
        using var client = fixture.CreateBriefHttpClient();
        (await fixture.WaitForBriefMembershipAsync(0, Ct)).SubscriptionCount.ShouldBe(0);
        fixture.AiListenerMembership.SubscriptionCount.ShouldBe(2);
        foreach (
            var path in new[]
            {
                "/api/trips/brief-extraction-capability",
                "/API/TRIPS/BRIEF-EXTRACTION-CAPABILITY/",
                "/api/trips/brief-candidate",
                "/API/TRIPS/BRIEF-CANDIDATE/",
            }
        )
        {
            using var response = await Send(
                client,
                path.Contains("capability", StringComparison.OrdinalIgnoreCase)
                    ? HttpMethod.Get
                    : HttpMethod.Post,
                path,
                "{}",
                authenticated: false
            );
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            AssertNoStore(response);
        }
        fixture.BriefCalls.ShouldBe(0);
    }

    internal static string Body(Guid requestId, string text, string? policy = null) =>
        JsonSerializer.Serialize(
            new
            {
                requestId,
                text,
                locale = "ru",
                referenceZone = "Europe/Moscow",
                processingPolicyVersion = policy ?? NlSearchTransportFixture.BriefPolicy,
            }
        );

    internal static async Task<HttpResponseMessage> Send(
        HttpClient client,
        HttpMethod method,
        string path,
        string? body = null,
        bool authenticated = true,
        bool untrusted = false
    )
    {
        using var request = new HttpRequestMessage(method, path);
        if (authenticated)
            request.Headers.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        if (untrusted)
            request.Headers.Add("X-Brief-Identity", "untrusted");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request, Ct);
    }

    internal static void AssertNoStore(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }
}
