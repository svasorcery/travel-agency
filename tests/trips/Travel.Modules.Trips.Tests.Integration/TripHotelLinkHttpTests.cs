using System.Net;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Application.HotelLinks;
using Travel.Modules.Trips.Core;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripHotelLinkHttpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string AccessHeader = "X-Trips-Hotels-Access";

    [Fact]
    public async Task Disabled_route_returns_not_found_before_resolving_link_dependencies()
    {
        await using var fixture = await TripsHttpFixture.Start(configure: services =>
            services.AddScoped<TripHotelLinkService>(_ =>
                throw new InvalidOperationException("Disabled link service was resolved.")
            )
        );
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner").ShouldBeTrue();
        using var response = await fixture.Client.GetAsync(
            "/api/trips/hotel-link/availability",
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Separate_status_revalidates_facts_and_owner_can_unlink_without_Hotels()
    {
        var storage = new FakeStore();
        var source = new FakeSource();
        await using var fixture = await Start(storage, source);
        var client = fixture.Client;
        var plan = await Create(fixture, storage);
        var link = plan + "/hotel-link";
        client.DefaultRequestHeaders.Add(AccessHeader, "true");
        var id = source.ReservationId;

        using var add = await Patch(
            client,
            link,
            $$"""{"op":"add","reservationId":"{{id:D}}"}""",
            "\"hl-0\""
        );
        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        add.Headers.ETag!.Tag.ShouldBe("\"hl-1\"");
        add.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var metadata = await add.Content.ReadAsStringAsync(Ct);
        metadata.ShouldNotContain(id.ToString());
        JsonNode.Parse(metadata)!["hasLink"]!.GetValue<bool>().ShouldBeTrue();

        using var first = await client.GetAsync(link + "/status", Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag.ShouldBeNull();
        var confirmed = JsonNode.Parse(await first.Content.ReadAsStringAsync(Ct))!;
        confirmed["state"]!.GetValue<string>().ShouldBe("confirmed");
        confirmed["checkOut"]!.GetValue<string>().ShouldBe("2027-06-15");

        source.State = HotelTripReservationState.Cancelled;
        using var conditional = new HttpRequestMessage(HttpMethod.Get, link + "/status");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", "\"hl-1\"");
        using var cancelled = await client.SendAsync(conditional, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelledBody = JsonNode.Parse(await cancelled.Content.ReadAsStringAsync(Ct))!;
        cancelledBody["state"]!.GetValue<string>().ShouldBe("cancelled");
        cancelledBody["revision"]!.GetValue<string>().ShouldBe("1");
        cancelledBody.AsObject().ContainsKey("reservationId").ShouldBeFalse();

        client.DefaultRequestHeaders.Remove(AccessHeader).ShouldBeTrue();
        source.IsAvailable = false;
        var reads = source.Reads;
        using var restricted = await client.GetAsync(link + "/status", Ct);
        var restrictedText = await restricted.Content.ReadAsStringAsync(Ct);
        restrictedText.ShouldContain("unavailable");
        restrictedText.ShouldNotContain(id.ToString());
        using var unavailable = await client.GetAsync("/api/trips/hotel-link/availability", Ct);
        var availability = JsonNode.Parse(await unavailable.Content.ReadAsStringAsync(Ct))!;
        availability["enabled"]!.GetValue<bool>().ShouldBeTrue();
        availability["canAttach"]!.GetValue<bool>().ShouldBeFalse();
        using var removed = await Patch(client, link, """{"op":"remove"}""", "\"hl-1\"");
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        removed.Headers.ETag!.Tag.ShouldBe("\"hl-2\"");
        source.Reads.ShouldBe(reads);
        using var persistedPlan = await client.GetAsync(plan, Ct);
        persistedPlan.Headers.ETag!.Tag.ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Request_shape_preconditions_and_owner_policy_hide_unknown_orders()
    {
        var storage = new FakeStore();
        var source = new FakeSource();
        await using var fixture = await Start(storage, source);
        var client = fixture.Client;
        var link = await Create(fixture, storage) + "/hotel-link";
        var addBody = $$"""{"op":"add","reservationId":"{{source.ReservationId:D}}"}""";
        using var noHotelPermission = await Patch(client, link, addBody, "\"hl-0\"");
        noHotelPermission.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add(AccessHeader, "true");
        using var noTag = await Patch(client, link, addBody, null);
        noTag.StatusCode.ShouldBe((HttpStatusCode)428);
        using var weak = await Patch(client, link, addBody, "W/\"hl-0\"");
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var malformed = await Patch(
            client,
            link,
            """{"op":"remove","op":"remove"}""",
            "\"hl-0\""
        );
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var stale = await Patch(client, link, addBody, "\"hl-1\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var foreignTrip = await Patch(
            client,
            "/api/trips/" + Guid.NewGuid() + "/hotel-link",
            addBody,
            "\"hl-0\""
        );
        foreignTrip.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        source.Reads.ShouldBe(0);

        var missingBody = $$"""{"op":"add","reservationId":"{{Guid.NewGuid():D}}"}""";
        using var emptySlot = await Patch(client, link, missingBody, "\"hl-0\"");
        emptySlot.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var attached = await Patch(client, link, addBody, "\"hl-0\"");
        attached.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var occupied = await Patch(client, link, missingBody, "\"hl-1\"");
        occupied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var codeBefore = JsonNode.Parse(await emptySlot.Content.ReadAsStringAsync(Ct))![
            "code"
        ]!.GetValue<string>();
        JsonNode.Parse(await occupied.Content.ReadAsStringAsync(Ct))!["code"]!
            .GetValue<string>()
            .ShouldBe(codeBefore);
    }

    private static Task<TripsHttpFixture> Start(FakeStore store, FakeSource source) =>
        TripsHttpFixture.Start(
            environment: "Testing",
            configure: services =>
            {
                services.AddSingleton(new TripHotelLinksFeature(true));
                services.AddSingleton<ITripHotelLinkStore>(store);
                services.AddSingleton<ITripHotelLinkProtector, FakeProtector>();
                services.AddSingleton(new Func<IHotelsTripsReservationAccess?>(() => source));
                services.AddScoped<TripHotelLinkService>();
                services.AddAuthorization(options =>
                    options.AddPolicy(
                        "hotels:book",
                        policy =>
                            policy.RequireAssertion(c =>
                                c.Resource is HttpContext context
                                && context.Request.Headers.ContainsKey(AccessHeader)
                            )
                    )
                );
            }
        );

    private static async Task<string> Create(TripsHttpFixture fixture, FakeStore store)
    {
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(TripsHttpTests.CreateJson),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var url = created.Headers.Location!.ToString();
        store.TripId = Guid.Parse(url.Split('/').Last());
        return url;
    }

    private static async Task<HttpResponseMessage> Patch(
        HttpClient client,
        string path,
        string body,
        string? tag
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = TripsHttpTests.Body(body),
        };
        if (tag is not null)
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        return await client.SendAsync(request, Ct);
    }

    private sealed class FakeStore : ITripHotelLinkStore
    {
        public Guid TripId { get; set; }
        private TripHotelLinkRecord? row;

        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<ErrorOr<TripHotelLinkRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid tripId,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<TripHotelLinkRecord?>>(
                owner.Subject == TripsHttpFixture.Owner && tripId == TripId
                    ? row
                    : TripErrors.NotFound
            );

        public Task<ErrorOr<Success>> CommitAsync(
            TripHotelLinkBinding expected,
            TripHotelLinkRecord? replacement,
            CancellationToken ct
        )
        {
            expected.Owner.Subject.ShouldBe(TripsHttpFixture.Owner);
            expected.TripId.ShouldBe(TripId);
            expected.Revision.ShouldBe(row?.Binding.Revision ?? 0);
            if (replacement is not null)
                row = replacement;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    private sealed class FakeProtector : ITripHotelLinkProtector
    {
        public bool IsReady => true;

        public ErrorOr<string> Protect(Guid? reservationId, TripHotelLinkBinding binding) =>
            reservationId?.ToString("D") ?? "-";

        public ErrorOr<Guid?> Unprotect(string envelope, TripHotelLinkBinding binding) =>
            envelope == "-" ? (Guid?)null : Guid.Parse(envelope);
    }

    private sealed class FakeSource : IHotelsTripsReservationAccess
    {
        public Guid ReservationId { get; } = Guid.NewGuid();
        public bool IsAvailable { get; set; } = true;
        public HotelTripReservationState State { get; set; } = HotelTripReservationState.Confirmed;
        public int Reads { get; private set; }

        public Task<ErrorOr<HotelTripReservationObservation>> ReadAsync(
            Guid ownerId,
            Guid reservationId,
            CancellationToken ct = default
        )
        {
            Reads++;
            return Task.FromResult<ErrorOr<HotelTripReservationObservation>>(
                ownerId == TripsHttpFixture.Owner && reservationId == ReservationId
                    ? State == HotelTripReservationState.Confirmed
                        ? new HotelTripReservationObservation(
                            State,
                            "Fictional stay",
                            new(2027, 6, 13),
                            new(2027, 6, 15)
                        )
                        : new HotelTripReservationObservation(State)
                    : Error.NotFound("Hotels.Missing")
            );
        }
    }
}
