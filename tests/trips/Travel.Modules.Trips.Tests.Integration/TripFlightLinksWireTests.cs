using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripFlightLinksWireTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Patch_and_get_share_one_version_and_status_only_exposes_current_owner_observation()
    {
        var fixture = new Data();
        await using var app = await TripsHttpFixture.Start(
            flightLinks: true,
            configure: s =>
            {
                s.AddSingleton<ITripFlightLinkStore>(fixture);
                s.AddSingleton<ITripFlightLinkProtector>(fixture);
                s.AddSingleton<ITripFlightLinkCodec, TripFlightLinkCodec>();
                s.AddSingleton<Func<TripFlightLinkSource>>(() => new(fixture));
                s.AddScoped<TripFlightLinkService>();
            }
        );
        var path = "/api/trips/" + fixture.Trip + "/flight-links";
        using var old = await app.Client.GetAsync(path, Ct);
        old.Headers.ETag!.Tag.ShouldBe("\"fl-0\"");
        async Task<HttpResponseMessage> Change(string op, string etag)
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, path)
            {
                Content = JsonContent.Create(new { op, orderId = fixture.Order }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await app.Client.SendAsync(request, Ct);
        }
        using var first = await Change("add", old.Headers.ETag.Tag);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        first.Headers.ETag!.Tag.ShouldBe("\"fl-1\"");
        using var same = await Change("add", first.Headers.ETag.Tag);
        same.Headers.ETag!.Tag.ShouldBe(first.Headers.ETag.Tag);
        fixture.Calls.ShouldBe(1);
        using var page = await app.Client.GetFromJsonAsync<JsonDocument>(
            "/api/trips/" + fixture.Trip + "/flight-order-candidates?offset=0",
            Ct
        );
        page!
            .RootElement.GetProperty("items")[0]
            .GetProperty("orderId")
            .GetGuid()
            .ShouldBe(fixture.Order);
        using var status = await app.Client.GetFromJsonAsync<JsonDocument>(path + "/status", Ct);
        var item = status!.RootElement.GetProperty("items")[0];
        item.GetProperty("state").GetString().ShouldBe("available");
        item.GetProperty("bookingStatus").GetString().ShouldBe("Held");
        item.GetProperty("streamVersion").GetString().ShouldBe("2");
        item.GetProperty("route")[0]
            .GetProperty("departAt")
            .GetDateTimeOffset()
            .Offset.ShouldBe(TimeSpan.FromHours(3));
        fixture.Unavailable = true;
        using var failed = await app.Client.GetFromJsonAsync<JsonDocument>(path + "/status", Ct);
        var failure = failed!.RootElement.GetProperty("items")[0];
        failure.GetProperty("state").GetString().ShouldBe("unavailable");
        failure.EnumerateObject().Count().ShouldBe(2);
        using var stale = await Change("remove", old.Headers.ETag.Tag);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        using var detached = await Change("remove", same.Headers.ETag.Tag);
        detached.Headers.ETag!.Tag.ShouldBe("\"fl-2\"");
        fixture.Calls.ShouldBe(3);
        using var reopened = await app.Client.GetFromJsonAsync<JsonDocument>(path, Ct);
        reopened!.RootElement.GetProperty("links").GetArrayLength().ShouldBe(0);
        reopened.RootElement.GetProperty("linksRevision").GetString().ShouldBe("2");
    }

    private sealed class Data
        : ITripFlightLinkProtector,
            ITripFlightLinkStore,
            IFlightsTripsOrderAccess
    {
        public readonly Guid Trip = Guid.NewGuid(),
            Order = Guid.NewGuid();
        public bool Unavailable { get; set; }
        public int Calls { get; private set; }
        private TripFlightLinkRecord? row;
        public bool IsReady => true;

        public ErrorOr<string> Protect(string plaintext, TripFlightLinkBinding binding) =>
            plaintext;

        public ErrorOr<string> Unprotect(string envelope, TripFlightLinkBinding binding) =>
            envelope;

        public Task<ErrorOr<TripFlightLinkRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid tripId,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<TripFlightLinkRecord?>>(
                owner.Subject == TripsHttpFixture.Owner && tripId == Trip
                    ? row
                    : TripErrors.NotFound
            );

        public Task<ErrorOr<Success>> CommitAsync(
            TripFlightLinkBinding expected,
            TripFlightLinkRecord? replacement,
            CancellationToken ct
        )
        {
            if ((row?.Binding.Revision ?? 0) != expected.Revision)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            row = replacement ?? row;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<FlightOrderObservation>> ReadAsync(
            Guid ownerId,
            Guid orderId,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            var now = DateTimeOffset.UtcNow;
            var departure = now.ToOffset(TimeSpan.FromHours(3));
            return Task.FromResult<ErrorOr<FlightOrderObservation>>(
                Unavailable
                    ? Error.Failure()
                    : new FlightOrderObservation(
                        orderId,
                        ownerId,
                        2,
                        now,
                        FlightOrderBookingStatus.Held,
                        false,
                        1,
                        [new("LED", "SVO", departure, departure.AddHours(2))]
                    )
            );
        }

        public Task<ErrorOr<FlightOrderCandidatePage>> ListCandidatesAsync(
            Guid ownerId,
            int offset,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<ErrorOr<FlightOrderCandidatePage>>(
                new FlightOrderCandidatePage(
                    [new(Order, FlightOrderBookingStatus.Held, 1, null)],
                    null,
                    false
                )
            );
    }
}
