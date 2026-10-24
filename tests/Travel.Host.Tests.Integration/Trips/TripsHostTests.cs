using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripsHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static object CreateBody =>
        new
        {
            title = "Fictional Moscow private plan",
            brief = new
            {
                destinationId = "moscow",
                originId = (string?)null,
                startDate = "2026-10-25",
                endDate = "2026-10-25",
                adults = 2,
                currency = "RUB",
                totalBudget = (string?)null,
                interests = new[] { "history", "nature" },
                pace = "balanced",
            },
        };

    [Fact]
    public async Task Actual_Host_creates_protected_plan_and_preserves_edits_CAS_owner_and_restart()
    {
        await using var fixture = new TripsHostFixture();
        await fixture.StartAsync(Ct);
        using var created = await fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plan = JsonNode.Parse(await created.Content.ReadAsStringAsync(Ct))!;
        var id = plan["tripId"]!.GetValue<string>();
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.Location!.OriginalString.ShouldBe("/api/trips/" + id);
        var blockId = Guid.NewGuid();
        var change = new
        {
            title = "Fictional edited private plan",
            brief = plan["brief"]!.DeepClone(),
            days = new[]
            {
                new
                {
                    date = "2026-10-25",
                    blocks = new[]
                    {
                        new
                        {
                            blockId,
                            title = "Fictional garden",
                            note = "Private fictional note",
                            startTime = "10:00",
                            endTime = "10:45",
                            estimate = new { amount = "450.00", currency = "RUB" },
                        },
                    },
                },
            },
        };
        using var put = Write(id, "1", change);
        using var saved = await fixture.Client.SendAsync(put, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var expected = await saved.Content.ReadAsStringAsync(Ct);
        var savedPlan = JsonNode.Parse(expected)!;
        savedPlan["days"]![0]!["blocks"]![0]!["blockId"]!.GetValue<Guid>().ShouldBe(blockId);
        savedPlan["knownUserEstimateSubtotal"]!.GetValue<string>().ShouldBe("450.00");
        string envelope;
        await using (var db = fixture.Db())
        {
            var row = await db.Plans.AsNoTracking().SingleAsync(Ct);
            row.Revision.ShouldBe(2);
            row.OwnerIssuer.ShouldBe(TripsHostFixture.Issuer);
            row.OwnerSubject.ShouldBe(TripsHostFixture.Owner);
            row.ProtectedPayload.ShouldNotContain("Private fictional note");
            row.ProtectedPayload.ShouldNotContain("Fictional edited private plan");
            envelope = row.ProtectedPayload;
        }
        using var stale = Write(id, "1", change);
        using var staleResult = await fixture.Client.SendAsync(stale, Ct);
        staleResult.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/trips/" + id);
        foreign.Headers.Add("X-Trips-Owner", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        using var foreignResult = await fixture.Client.SendAsync(foreign, Ct);
        foreignResult.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await fixture.RestartAsync();
        using var reopened = await fixture.Client.GetAsync("/api/trips/" + id, Ct);
        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reopened.Content.ReadAsStringAsync(Ct)).ShouldBe(expected);
        reopened.Headers.ETag!.Tag.ShouldBe("\"2\"");
        await using var verify = fixture.Db();
        (await verify.Plans.AsNoTracking().SingleAsync(Ct)).ProtectedPayload.ShouldBe(envelope);
    }

    [Fact]
    public async Task Actual_Host_concurrent_last_slot_keeps_store_owned_capacity_transaction_atomic()
    {
        await using var fixture = new TripsHostFixture();
        await fixture.StartAsync(Ct);
        await using (var db = fixture.Db())
        {
            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < 99; index++)
                db.Plans.Add(
                    new TripPlanEntity
                    {
                        TripId = Guid.NewGuid(),
                        OwnerIssuer = TripsHostFixture.Issuer,
                        OwnerSubject = TripsHostFixture.Owner,
                        Revision = 1,
                        CreatedAt = now,
                        UpdatedAt = now,
                        ProtectedPayload = "fictional capacity-only sentinel",
                    }
                );
            await db.SaveChangesAsync(Ct);
        }
        var responses = await Task.WhenAll(
            fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct),
            fixture.Client.PostAsJsonAsync("/api/trips", CreateBody, Ct)
        );
        try
        {
            responses.Count(x => x.StatusCode == HttpStatusCode.Created).ShouldBe(1);
            responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
            var refused = responses.Single(x => x.StatusCode == HttpStatusCode.Conflict);
            JsonNode.Parse(await refused.Content.ReadAsStringAsync(Ct))!["code"]!
                .GetValue<string>()
                .ShouldBe("Trips.CapacityExceeded");
            await using var verify = fixture.Db();
            (await verify.Plans.CountAsync(Ct)).ShouldBe(100);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    private static HttpRequestMessage Write(string id, string revision, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/trips/" + id)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"" + revision + "\"");
        return request;
    }
}
