using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripFlightLinkServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner => TripPlanTests.Owner;

    [Theory]
    [InlineData("owner")]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("default-time")]
    [InlineData("offset-time")]
    [InlineData("future-time")]
    [InlineData("status")]
    [InlineData("empty-route")]
    [InlineData("airport")]
    [InlineData("party")]
    public async Task Rejects_misbound_or_malformed_proof_before_commit(string fault)
    {
        var f = new Fixture();
        f.Observe = id =>
            fault switch
            {
                "owner" => Proof(id) with { OwnerId = Guid.NewGuid() },
                "id" => Proof(Guid.NewGuid()),
                "version" => Proof(id) with { StreamVersion = 0 },
                "default-time" => Proof(id) with { ObservedAt = default },
                "offset-time" => Proof(id) with
                {
                    ObservedAt = TripPlanTests.Now.ToOffset(TimeSpan.FromHours(3)),
                },
                "future-time" => Proof(id) with { ObservedAt = DateTimeOffset.MaxValue },
                "status" => Proof(id) with { BookingStatus = (FlightOrderBookingStatus)47 },
                "empty-route" => Proof(id) with { Route = [] },
                "airport" => Proof(id) with
                {
                    Route = [new("???", "LED", TripPlanTests.Now, TripPlanTests.Now.AddHours(1))],
                },
                _ => Proof(id) with { PassengerCount = 10 },
            };
        var result = await f.Service.PatchAsync(Owner, f.Trip, 0, true, Guid.NewGuid(), Ct);
        result.FirstError.ShouldBe(TripErrors.Unavailable);
        f.SourceReads.ShouldBe(1);
        f.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Metadata_foreign_parent_checked_noops_and_remove_need_no_source()
    {
        var f = new Fixture();
        var s = f.Service;
        var id = Guid.NewGuid();
        var foreign = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        (await s.GetAsync(Owner, f.Trip, Ct)).Value.Revision.ShouldBe(0);
        (await s.GetAsync(foreign, f.Trip, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (await s.StatusAsync(foreign, f.Trip, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (await s.StatusAsync(Owner, f.Trip, Ct)).Value.Items.ShouldBeEmpty();
        (await s.CandidatesAsync(foreign, f.Trip, 0, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (await s.PatchAsync(foreign, f.Trip, 0, true, id, Ct)).FirstError.ShouldBe(
            TripErrors.NotFound
        );
        (await s.PatchAsync(Owner, f.Trip, 0, false, id, Ct)).Value.Revision.ShouldBe(0);
        f.Resolutions.ShouldBe(0);
        (await s.PatchAsync(Owner, f.Trip, 0, true, id, Ct)).Value.Revision.ShouldBe(1);
        (await s.PatchAsync(Owner, f.Trip, 0, true, id, Ct)).FirstError.ShouldBe(
            TripErrors.RevisionMismatch
        );
        (await s.PatchAsync(Owner, f.Trip, 1, true, id, Ct)).Value.Revision.ShouldBe(1);
        f.RejectCommit = true;
        (await s.PatchAsync(Owner, f.Trip, 1, true, id, Ct)).FirstError.ShouldBe(
            TripErrors.RevisionMismatch
        );
        f.RejectCommit = false;
        (await s.PatchAsync(Owner, f.Trip, 1, false, id, Ct)).Value.Revision.ShouldBe(2);
        (await s.GetAsync(Owner, f.Trip, Ct)).Value.Links.ShouldBeEmpty();
        f.Resolutions.ShouldBe(1);
        f.Commits.ShouldBe(5);
    }

    [Fact]
    public async Task Rejects_fifth_without_lookup_and_observes_one_item_at_a_time_in_link_order()
    {
        var f = new Fixture();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < 4; i++)
            (
                await f.Service.PatchAsync(Owner, f.Trip, i, true, ids[i], Ct)
            ).Value.Revision.ShouldBe(i + 1);
        (
            await f.Service.PatchAsync(Owner, f.Trip, 4, true, Guid.NewGuid(), Ct)
        ).FirstError.ShouldBe(TripErrors.FlightLinkLimitReached);
        f.SourceReads.ShouldBe(4);
        f.Observe = id =>
            id == ids[0] ? TripErrors.NotFound
            : id == ids[1] ? TripErrors.Unavailable
            : Proof(id);
        var result = (await f.Service.StatusAsync(Owner, f.Trip, Ct)).Value;
        result.LinksRevision.ShouldBe(4);
        result.Items.Select(x => x.OrderId).ShouldBe(ids);
        result
            .Items.Select(x => x.State)
            .ShouldBe(new[] { "notFound", "unavailable", "available", "available" });
        result.Items[0].Observation.ShouldBeNull();
        result.Items[1].Observation.ShouldBeNull();
        result.Items[2].Observation!.BookingStatus.ShouldBe("Held");
        f.SourceReads.ShouldBe(8);
        f.Commits.ShouldBe(4);
    }

    [Fact]
    public async Task Late_source_cancellation_and_final_store_conflict_do_not_retry()
    {
        var f = new Fixture();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        f.Observe = id =>
        {
            deadline.Cancel();
            return Proof(id);
        };
        await Should.ThrowAsync<OperationCanceledException>(() =>
            f.Service.PatchAsync(Owner, f.Trip, 0, true, Guid.NewGuid(), deadline.Token)
        );
        f.Commits.ShouldBe(0);
        f.Observe = id => Proof(id);
        f.RejectCommit = true;
        (
            await f.Service.PatchAsync(Owner, f.Trip, 0, true, Guid.NewGuid(), Ct)
        ).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        f.SourceReads.ShouldBe(2);
        f.Commits.ShouldBe(1);
    }

    private static FlightOrderObservation Proof(Guid id) =>
        new(id, Owner.Subject, 8, TripPlanTests.Now, FlightOrderBookingStatus.Held, false, 2, null);

    [Fact]
    public async Task Absent_order_is_concealed_and_late_commit_has_no_second_write()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        f.Observe = _ => TripErrors.NotFound;
        (await f.Service.PatchAsync(Owner, f.Trip, 0, true, id, Ct)).FirstError.ShouldBe(
            TripErrors.NotFound
        );
        f.Commits.ShouldBe(0);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        f.Observe = id => Proof(id);
        f.AfterCommit = deadline.Cancel;
        await Should.ThrowAsync<OperationCanceledException>(() =>
            f.Service.PatchAsync(Owner, f.Trip, 0, true, id, deadline.Token)
        );
        (await f.Service.GetAsync(Owner, f.Trip, Ct)).Value.Revision.ShouldBe(1);
        f.Commits.ShouldBe(1);
        f.SourceReads.ShouldBe(2);
    }

    [Fact]
    public async Task Candidates_validate_bounds_and_preserve_unknown_route_without_source_admission()
    {
        var f = new Fixture();
        var item = new FlightOrderCandidate(
            Guid.NewGuid(),
            FlightOrderBookingStatus.Cancelled,
            2,
            null
        );
        f.Page = new([item], null, false);
        (await f.Service.CandidatesAsync(Owner, f.Trip, 10001, Ct)).FirstError.ShouldBe(
            TripErrors.InvalidInput
        );
        f.Resolutions.ShouldBe(0);
        var result = (await f.Service.CandidatesAsync(Owner, f.Trip, 10000, Ct)).Value;
        result.Items.Single().OrderId.ShouldBe(item.OrderId);
        result.Items.Single().BookingStatus.ShouldBe("Cancelled");
        result.Items.Single().Route.ShouldBeNull();
        f.Page = new([item, item], null, false);
        (await f.Service.CandidatesAsync(Owner, f.Trip, 0, Ct)).FirstError.ShouldBe(
            TripErrors.Unavailable
        );
        f.Page = new([item], 20, false);
        (await f.Service.CandidatesAsync(Owner, f.Trip, 0, Ct)).FirstError.ShouldBe(
            TripErrors.Unavailable
        );
        f.Commits.ShouldBe(0);
        f.SourceReads.ShouldBe(0);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TripPlanTests.Now;
    }

    private sealed class Fixture
        : ITripFlightLinkStore,
            ITripFlightLinkProtector,
            ITripFlightLinkCodec,
            IFlightsTripsOrderAccess
    {
        private TripFlightLinkRecord? row;
        private readonly Dictionary<string, TripFlightLinks> snapshots = [];
        public Guid Trip { get; } = Guid.NewGuid();
        public int Resolutions { get; private set; }
        public int SourceReads { get; private set; }
        public int Commits { get; private set; }
        public bool RejectCommit { get; set; }
        public Action? AfterCommit { get; set; }
        public FlightOrderCandidatePage? Page { get; set; }
        public Func<Guid, ErrorOr<FlightOrderObservation>> Observe { get; set; } = id => Proof(id);
        public TripFlightLinkService Service =>
            new(
                this,
                this,
                this,
                () =>
                {
                    Resolutions++;
                    return new(this);
                },
                new Clock()
            );

        public Task<ErrorOr<TripFlightLinkRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid tripId,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<TripFlightLinkRecord?>>(
                owner == Owner && tripId == Trip ? row : TripErrors.NotFound
            );

        public Task<ErrorOr<Success>> CommitAsync(
            TripFlightLinkBinding expected,
            TripFlightLinkRecord? replacement,
            CancellationToken ct
        )
        {
            Commits++;
            if (RejectCommit || (row?.Binding.Revision ?? 0) != expected.Revision)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            row = replacement ?? row;
            AfterCommit?.Invoke();
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public bool IsReady => true;

        public ErrorOr<string> Protect(string plaintext, TripFlightLinkBinding binding) =>
            plaintext;

        public ErrorOr<string> Unprotect(string envelope, TripFlightLinkBinding binding) =>
            envelope;

        public ErrorOr<string> Serialize(TripFlightLinks links)
        {
            var handle = Guid.NewGuid().ToString();
            snapshots.Add(handle, links);
            return handle;
        }

        public ErrorOr<TripFlightLinks> Deserialize(
            string plaintext,
            TripFlightLinkBinding binding
        ) => snapshots[plaintext];

        public Task<ErrorOr<FlightOrderObservation>> ReadAsync(
            Guid ownerId,
            Guid orderId,
            CancellationToken cancellationToken
        )
        {
            ownerId.ShouldBe(Owner.Subject);
            SourceReads++;
            return Task.FromResult(Observe(orderId));
        }

        public Task<ErrorOr<FlightOrderCandidatePage>> ListCandidatesAsync(
            Guid ownerId,
            int offset,
            CancellationToken cancellationToken
        )
        {
            ownerId.ShouldBe(Owner.Subject);
            return Task.FromResult<ErrorOr<FlightOrderCandidatePage>>(
                Page ?? throw new InvalidOperationException("Unexpected candidate read.")
            );
        }
    }
}
