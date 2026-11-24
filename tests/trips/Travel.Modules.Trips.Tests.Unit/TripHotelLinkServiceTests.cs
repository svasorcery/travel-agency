using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Trips.Application.HotelLinks;
using Travel.Modules.Trips.Core;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripHotelLinkServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner => TripPlanTests.Owner;
    private static readonly DateOnly CheckIn = new(2027, 6, 13);
    private static readonly DateOnly CheckOut = new(2027, 6, 15);
    private static HotelTripReservationObservation Confirmed =>
        new(HotelTripReservationState.Confirmed, "Fictional guesthouse", CheckIn, CheckOut);

    [Fact]
    public async Task Fresh_status_uses_the_current_Hotels_facts_at_the_same_link_revision()
    {
        var f = new Fixture();
        var saved = await f.AddAsync();
        saved.Value.ShouldBe(new TripHotelLinkMetadata(true, 1));
        (await f.Service.StatusAsync(Owner, f.TripId, true, Ct)).Value.ShouldBe(
            new TripHotelLinkStatus(
                1,
                "confirmed",
                f.ReservationId,
                "Fictional guesthouse",
                CheckIn,
                CheckOut
            )
        );

        f.Reader.Observation = new HotelTripReservationObservation(
            HotelTripReservationState.Cancelled
        );
        (await f.Service.StatusAsync(Owner, f.TripId, true, Ct)).Value.ShouldBe(
            new TripHotelLinkStatus(1, "cancelled")
        );
        (await f.Service.MetadataAsync(Owner, f.TripId, Ct)).Value.ShouldBe(saved.Value);
        f.Store.Writes.ShouldBe(1);
        f.Reader.Reads.ShouldBe(3);
    }

    [Fact]
    public async Task Same_id_no_op_requires_fresh_Hotels_proof_and_atomic_revision_check()
    {
        var f = new Fixture();
        (await f.AddAsync()).IsError.ShouldBeFalse();
        (await f.AddAsync(1)).Value.Revision.ShouldBe(1);
        f.Store.CheckedNoOps.ShouldBe(1);
        f.Reader.Observation = new HotelTripReservationObservation(
            HotelTripReservationState.ReviewRequired
        );
        (await f.AddAsync(1)).FirstError.Code.ShouldBe("Trips.HotelLinkOrderIneligible");
        f.Reader.Observation = HotelErrorsNotFound();
        foreach (var candidate in new[] { f.ReservationId, Guid.NewGuid() })
        {
            (
                await f.Service.PatchAsync(Owner, f.TripId, 1, candidate, true, Ct)
            ).FirstError.ShouldBe(TripErrors.NotFound);
        }
        f.Reader.Reads.ShouldBe(5);
        f.Store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Link_can_be_removed_after_Hotels_access_is_lost_and_empty_revision_is_retained()
    {
        var f = new Fixture();
        (await f.AddAsync()).IsError.ShouldBeFalse();
        f.Reader.IsAvailable = false;
        (await f.Service.StatusAsync(Owner, f.TripId, false, Ct)).Value.ShouldBe(
            new TripHotelLinkStatus(1, "unavailable")
        );
        (await f.Service.PatchAsync(Owner, f.TripId, 1, null, false, Ct)).Value.ShouldBe(
            new TripHotelLinkMetadata(false, 2)
        );
        (await f.AddAsync(0)).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        (await f.Service.StatusAsync(Owner, f.TripId, true, Ct)).Value.ShouldBe(
            new TripHotelLinkStatus(2, "empty")
        );
        f.Reader.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Ownership_and_stale_revision_are_resolved_before_a_Hotels_read()
    {
        var f = new Fixture();
        var other = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        (
            await f.Service.PatchAsync(other, f.TripId, 0, f.ReservationId, true, Ct)
        ).FirstError.ShouldBe(TripErrors.NotFound);
        (await f.AddAsync(1)).FirstError.ShouldBe(TripErrors.RevisionMismatch);
        (
            await f.Service.PatchAsync(Owner, f.TripId, 0, f.ReservationId, false, Ct)
        ).FirstError.ShouldBe(TripErrors.Forbidden);
        f.Reader.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task A_different_eligible_order_cannot_replace_an_occupied_association()
    {
        var f = new Fixture();
        (await f.AddAsync()).IsError.ShouldBeFalse();
        var otherId = Guid.NewGuid();
        (
            await f.Service.PatchAsync(Owner, f.TripId, 1, otherId, true, Ct)
        ).FirstError.Code.ShouldBe("Trips.HotelLinkOccupied");
        (await f.Service.StatusAsync(Owner, f.TripId, true, Ct)).Value.ReservationId.ShouldBe(
            f.ReservationId
        );
        f.Store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Reader_exception_does_not_restore_private_facts_or_advance_revision()
    {
        var f = new Fixture();
        (await f.AddAsync()).IsError.ShouldBeFalse();
        f.Reader.ThrowOnRead = true;
        (await f.Service.StatusAsync(Owner, f.TripId, true, Ct)).Value.ShouldBe(
            new TripHotelLinkStatus(1, "unavailable")
        );
        (await f.AddAsync(1)).FirstError.ShouldBe(TripErrors.Unavailable);
        f.Store.Writes.ShouldBe(1);
    }

    private static Error HotelErrorsNotFound() => Error.NotFound("Hotels.Missing");

    private sealed class Fixture
    {
        internal Guid TripId { get; } = Guid.NewGuid();
        internal Guid ReservationId { get; } = Guid.NewGuid();
        internal Reader Reader { get; } = new();
        internal Store Store { get; }
        internal TripHotelLinkService Service { get; }

        internal Fixture()
        {
            Store = new(TripId);
            Service = new(Store, new Protector(), () => Reader);
        }

        internal Task<ErrorOr<TripHotelLinkMetadata>> AddAsync(long revision = 0) =>
            Service.PatchAsync(Owner, TripId, revision, ReservationId, true, Ct);
    }

    private sealed class Reader : IHotelsTripsReservationAccess
    {
        internal int Reads { get; private set; }
        public bool IsAvailable { get; set; } = true;
        internal bool ThrowOnRead { get; set; }
        internal ErrorOr<HotelTripReservationObservation> Observation { get; set; } = Confirmed;

        public Task<ErrorOr<HotelTripReservationObservation>> ReadAsync(
            Guid ownerId,
            Guid reservationId,
            CancellationToken ct = default
        )
        {
            Reads++;
            if (ThrowOnRead)
                throw new InvalidOperationException("Fictional reader failure.");
            return Task.FromResult(Observation);
        }
    }

    private sealed class Store(Guid ownedTrip) : ITripHotelLinkStore
    {
        private TripHotelLinkRecord? row;
        internal int Writes { get; private set; }
        internal int CheckedNoOps { get; private set; }

        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<ErrorOr<TripHotelLinkRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid tripId,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<TripHotelLinkRecord?>>(
                owner == Owner && tripId == ownedTrip ? row : TripErrors.NotFound
            );

        public Task<ErrorOr<Success>> CommitAsync(
            TripHotelLinkBinding expected,
            TripHotelLinkRecord? replacement,
            CancellationToken ct
        )
        {
            expected.Revision.ShouldBe(row?.Binding.Revision ?? 0);
            if (replacement is null)
                CheckedNoOps++;
            else
            {
                row = replacement;
                Writes++;
            }
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    private sealed class Protector : ITripHotelLinkProtector
    {
        public bool IsReady => true;

        public ErrorOr<string> Protect(Guid? reservationId, TripHotelLinkBinding binding) =>
            reservationId?.ToString("D") ?? "-";

        public ErrorOr<Guid?> Unprotect(string envelope, TripHotelLinkBinding binding) =>
            envelope == "-" ? (Guid?)null : Guid.Parse(envelope);
    }
}
