using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Application.Trips;
using Travel.Modules.Hotels.Core;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripHotelLinkHostTests
{
    private const string HotelName = "Fictional White Nights Guesthouse";
    private const string ManualStay = "Fictional manual stay";
    private const string Start = "2027-06-13";
    private const string End = "2027-06-15";

    [Fact]
    public async Task Host_reopens_the_link_and_preserves_the_plan_when_Hotels_access_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = ConfirmedReservation();
        var retained = new RetainedStore(row);
        var supplierCalls = 0;
        await using var host = new TripsHostFixture(
            hotelLinks: true,
            diagnostics: services =>
            {
                // Real Hotels reader over a fictional retained row. Trips and its HTTP/auth remain real.
                services.RemoveAll<IHotelsTripsReservationAccess>();
                services.AddScoped<IHotelsTripsReservationAccess>(
                    s => new HotelsTripsReservationAccess(
                        retained,
                        s.GetRequiredService<TimeProvider>(),
                        true
                    )
                );
                foreach (var name in new[] { "hotels-duffel", "hotels-hotelbeds" })
                    services
                        .AddHttpClient(name)
                        .ConfigurePrimaryHttpMessageHandler(() =>
                            new NoSupplier(() => Interlocked.Increment(ref supplierCalls))
                        );
            }
        );
        await host.StartAsync(ct);
        var brief = new
        {
            destinationId = "saint-petersburg",
            originId = (string?)null,
            startDate = Start,
            endDate = End,
            adults = 1,
            currency = "RUB",
            totalBudget = (string?)null,
            interests = new[] { "architecture" },
            pace = "balanced",
        };
        const string title = "Fictional Saint Petersburg trip";
        using var created = await host.Client.PostAsJsonAsync(
            "/api/trips",
            new { title, brief },
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var planPath = created.Headers.Location!.OriginalString;
        using var put = new HttpRequestMessage(HttpMethod.Put, planPath)
        {
            Content = JsonContent.Create(
                new
                {
                    title,
                    brief,
                    days = new[] { Start, "2027-06-14", End }
                        .Select(date => new { date, blocks = Array.Empty<object>() })
                        .ToArray(),
                    manualStay = new
                    {
                        placeName = ManualStay,
                        checkInDate = Start,
                        checkOutDate = End,
                    },
                }
            ),
        };
        put.Headers.IfMatch.Add(created.Headers.ETag!);
        using var saved = await host.Client.SendAsync(put, ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        saved.Headers.ETag!.Tag.ShouldBe("\"2\"");
        var savedPlan = await saved.Content.ReadAsStringAsync(ct);
        using (var planJson = JsonDocument.Parse(savedPlan))
            planJson
                .RootElement.GetProperty("manualStay")
                .GetProperty("placeName")
                .GetString()
                .ShouldBe(ManualStay);
        string snapshot;
        await using (var db = host.Db())
            snapshot = (await db.Plans.AsNoTracking().SingleAsync(ct)).ProtectedPayload;
        var keys = host.KeyHashes();

        var linkPath = planPath + "/hotel-link";
        var statusPath = linkPath + "/status";
        using var empty = await host.Client.GetAsync(linkPath, ct);
        await CheckLinkAsync(empty, false, "0", ct);
        using var add = WithHotelScope(HttpMethod.Patch, linkPath);
        add.Headers.IfMatch.Add(empty.Headers.ETag!);
        add.Content = JsonContent.Create(new { op = "add", reservationId = row.Id });
        using var added = await host.Client.SendAsync(add, ct);
        await CheckLinkAsync(added, true, "1", ct);
        retained.Reads.ShouldBe(1);

        await host.RestartAsync();
        host.KeyHashes().ShouldBe(keys);
        using var reopened = await host.Client.GetAsync(linkPath, ct);
        await CheckLinkAsync(reopened, true, "1", ct);
        using var view = WithHotelScope(HttpMethod.Get, statusPath);
        using var status = await host.Client.SendAsync(view, ct);
        var observed = await CheckStatusAsync(status, "confirmed", "1", ct);
        observed.GetProperty("reservationId").GetGuid().ShouldBe(row.Id);
        observed.GetProperty("propertyName").GetString().ShouldBe(HotelName);
        observed.GetProperty("checkIn").GetString().ShouldBe(Start);
        observed.GetProperty("checkOut").GetString().ShouldBe(End);
        observed
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ShouldBe(
                new[]
                {
                    "checkIn",
                    "checkOut",
                    "propertyName",
                    "reservationId",
                    "revision",
                    "state",
                }
            );
        retained.Reads.ShouldBe(2);

        using var foreign = WithHotelScope(HttpMethod.Get, statusPath);
        foreign.Headers.Add("X-Trips-Owner", "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        using var hidden = await host.Client.SendAsync(foreign, ct);
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var error = await hidden.Content.ReadAsStringAsync(ct);
        error.ShouldNotContain(row.Id.ToString());
        error.ShouldNotContain(HotelName);
        using var withoutScope = await host.Client.GetAsync(statusPath, ct);
        await CheckNoFactsAsync(withoutScope, "unavailable", "1", ct);
        retained.Reads.ShouldBe(2);

        row.PrivatePurgeAt = DateTimeOffset.MinValue;
        using var recheck = WithHotelScope(HttpMethod.Get, statusPath);
        recheck.Headers.IfNoneMatch.Add(reopened.Headers.ETag!);
        using var expired = await host.Client.SendAsync(recheck, ct);
        await CheckNoFactsAsync(expired, "unavailable", "1", ct);
        retained.Reads.ShouldBe(3);

        using var remove = new HttpRequestMessage(HttpMethod.Patch, linkPath)
        {
            Content = JsonContent.Create(new { op = "remove" }),
        };
        remove.Headers.IfMatch.Add(reopened.Headers.ETag!);
        using var removed = await host.Client.SendAsync(remove, ct);
        await CheckLinkAsync(removed, false, "2", ct);
        using var afterRemoval = await host.Client.GetAsync(statusPath, ct);
        await CheckNoFactsAsync(afterRemoval, "empty", "2", ct);
        retained.Reads.ShouldBe(3);

        using var current = await host.Client.GetAsync(planPath, ct);
        current.StatusCode.ShouldBe(HttpStatusCode.OK);
        current.Headers.ETag.ShouldBe(saved.Headers.ETag);
        (await current.Content.ReadAsStringAsync(ct)).ShouldBe(savedPlan);
        await using var verify = host.Db();
        var plan = await verify.Plans.AsNoTracking().SingleAsync(ct);
        plan.Revision.ShouldBe(2);
        plan.ProtectedPayload.ShouldBe(snapshot);
        row.Version.ShouldBe(7);
        row.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        supplierCalls.ShouldBe(0);
    }

    private static async Task CheckLinkAsync(
        HttpResponseMessage response,
        bool hasLink,
        string revision,
        CancellationToken ct
    )
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe($"\"hl-{revision}\"");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ShouldBe(new[] { "hasLink", "revision" });
        json.GetProperty("hasLink").GetBoolean().ShouldBe(hasLink);
        json.GetProperty("revision").GetString().ShouldBe(revision);
    }

    private static async Task<JsonElement> CheckStatusAsync(
        HttpResponseMessage response,
        string state,
        string revision,
        CancellationToken ct
    )
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("state").GetString().ShouldBe(state);
        json.GetProperty("revision").GetString().ShouldBe(revision);
        return json;
    }

    private static async Task CheckNoFactsAsync(
        HttpResponseMessage response,
        string state,
        string revision,
        CancellationToken ct
    )
    {
        var json = await CheckStatusAsync(response, state, revision, ct);
        json.EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ShouldBe(new[] { "revision", "state" });
    }

    private static HttpRequestMessage WithHotelScope(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Trips-Hotels-Scope", "true");
        return request;
    }

    private static HotelReservation ConfirmedReservation()
    {
        StayTerms accepted = new(
            new(
                "fictional-property",
                "Fictional quote name",
                "Private fictional street",
                null,
                null
            ),
            "Double",
            "fictional-rate",
            new(new(2027, 6, 13), new(2027, 6, 15)),
            new(1),
            new(4500m, "RUB"),
            [],
            [],
            new("NonRefundable", []),
            [],
            true,
            "ImmediateTest"
        );
        return new HotelReservation
        {
            Id = Guid.NewGuid(),
            OwnerId = TripsHostFixture.Owner,
            Terms = accepted,
            Version = 7,
            PrivatePurgeAt = DateTimeOffset.UtcNow.AddDays(1),
            State = new()
            {
                Outcome = ReservationOutcome.Confirmed,
                Facts = new(
                    "fictional-private-booking",
                    "fictional-private-reference",
                    accepted with
                    {
                        Property = accepted.Property with { Name = HotelName },
                    }
                ),
            },
        };
    }

    private sealed class RetainedStore(HotelReservation reservation) : IHotelReservationStore
    {
        public int Reads { get; private set; }

        public Task<ErrorOr<HotelReservation?>> LoadAsync(
            Guid id,
            Guid? owner,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult<ErrorOr<HotelReservation?>>(
                reservation.Id == id && reservation.OwnerId == owner ? reservation : null
            );
        }

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            throw new InvalidOperationException("A hotel link reads only one reservation.");

        public Task<ErrorOr<bool>> SaveAsync(
            HotelReservation reservation,
            int expectedVersion,
            HotelWork[] work,
            CancellationToken ct
        ) => throw new InvalidOperationException("A hotel link never changes a reservation.");
    }

    private sealed class NoSupplier(Action countCall) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            countCall();
            throw new InvalidOperationException("This test must use retained Hotels facts only.");
        }
    }
}
