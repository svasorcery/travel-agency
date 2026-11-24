using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Application.Trips;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelsTripsReservationAccessTests
{
    private const string FictionalName = "Гостевой дом «Белые ночи»";
    private static readonly DateTimeOffset Now = new(2027, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FictionalName)]
    [InlineData("Fictional Hotel 🏨")]
    public async Task Confirmed_read_uses_only_retained_actual_facts(string propertyName)
    {
        var row = ConfirmedRow(propertyName);
        var reader = new HotelsTripsReservationAccess(
            new RetainedStore(row),
            new FixedTime(),
            true
        );

        var result = await reader.ReadAsync(
            row.OwnerId,
            row.Id,
            TestContext.Current.CancellationToken
        );

        reader.IsAvailable.ShouldBeTrue();
        result.Value.ShouldBe(
            new HotelTripReservationObservation(
                HotelTripReservationState.Confirmed,
                propertyName,
                new DateOnly(2027, 6, 13),
                new DateOnly(2027, 6, 15)
            )
        );
        result.Value.ToString().ShouldBe(nameof(HotelTripReservationObservation));
    }

    [Fact]
    public async Task Retention_deadline_foreign_owner_and_missing_id_use_same_not_found()
    {
        var row = ConfirmedRow();
        var reader = new HotelsTripsReservationAccess(
            new RetainedStore(row),
            new FixedTime(),
            true
        );
        var ct = TestContext.Current.CancellationToken;

        var foreign = await reader.ReadAsync(Guid.NewGuid(), row.Id, ct);
        var missing = await reader.ReadAsync(row.OwnerId, Guid.NewGuid(), ct);
        row.PrivatePurgeAt = Now;
        var expired = await reader.ReadAsync(row.OwnerId, row.Id, ct);

        missing.FirstError.Type.ShouldBe(ErrorType.NotFound);
        foreign.FirstError.ShouldBe(missing.FirstError);
        expired.FirstError.ShouldBe(missing.FirstError);
    }

    [Theory]
    [InlineData(ReservationOutcome.Cancelled, HotelTripReservationState.Cancelled)]
    [InlineData(ReservationOutcome.Quoted, HotelTripReservationState.Ineligible)]
    [InlineData(ReservationOutcome.Pending, HotelTripReservationState.Ineligible)]
    [InlineData(ReservationOutcome.ReservedAwaitingDetails, HotelTripReservationState.Ineligible)]
    public async Task Nonconfirmed_states_have_no_dated_facts(
        ReservationOutcome outcome,
        HotelTripReservationState expected
    )
    {
        var row = ConfirmedRow();
        row.State.Outcome = outcome;

        var result = await new HotelsTripsReservationAccess(
            new RetainedStore(row),
            new FixedTime(),
            true
        ).ReadAsync(row.OwnerId, row.Id, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(new HotelTripReservationObservation(expected));
    }

    [Fact]
    public async Task Confirmed_order_with_review_reason_keeps_review_state_without_facts()
    {
        var row = ConfirmedRow();
        row.State.Review.Add(ReviewReason.TermsMismatch);

        var result = await new HotelsTripsReservationAccess(
            new RetainedStore(row),
            new FixedTime(),
            true
        ).ReadAsync(row.OwnerId, row.Id, TestContext.Current.CancellationToken);

        result.Value.ShouldBe(
            new HotelTripReservationObservation(HotelTripReservationState.ReviewRequired)
        );
    }

    [Theory]
    [InlineData("missing actual")]
    [InlineData("missing dates")]
    [InlineData("default check-in")]
    [InlineData("blank name")]
    [InlineData("blank with BOM")]
    [InlineData("control character")]
    [InlineData("malformed Unicode")]
    [InlineData("oversized name")]
    public async Task Unusable_actual_never_falls_back_to_accepted_terms(string invalidFact)
    {
        var row = ConfirmedRow();
        var facts = row.State.Facts!;
        var actual = facts.Actual!;
        row.State.Facts = facts with
        {
            Actual = invalidFact switch
            {
                "missing actual" => null,
                "missing dates" => actual with { Dates = null! },
                "default check-in" => actual with
                {
                    Dates = new(default, new DateOnly(2027, 6, 15)),
                },
                "blank name" => actual with { Property = actual.Property with { Name = " " } },
                "blank with BOM" => actual with
                {
                    Property = actual.Property with { Name = " \uFEFF " },
                },
                "control character" => actual with
                {
                    Property = actual.Property with { Name = "Hotel\u0000" },
                },
                "malformed Unicode" => actual with
                {
                    Property = actual.Property with { Name = "Hotel\uD800" },
                },
                "oversized name" => actual with
                {
                    Property = actual.Property with { Name = new('H', 4097) },
                },
                _ => throw new ArgumentException("Unknown test case.", nameof(invalidFact)),
            },
        };

        var result = await new HotelsTripsReservationAccess(
            new RetainedStore(row),
            new FixedTime(),
            true
        ).ReadAsync(row.OwnerId, row.Id, TestContext.Current.CancellationToken);

        result.Value.State.ShouldBe(HotelTripReservationState.Ineligible);
        result.Value.PropertyName.ShouldBeNull();
        result.Value.CheckIn.ShouldBeNull();
        result.Value.CheckOut.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Disabled_or_unavailable_store_releases_no_facts(bool enabled, int reads)
    {
        var row = ConfirmedRow();
        var store = new RetainedStore(row) { Failure = HotelErrors.Unavailable };
        var reader = new HotelsTripsReservationAccess(store, new FixedTime(), enabled);

        var result = await reader.ReadAsync(
            row.OwnerId,
            row.Id,
            TestContext.Current.CancellationToken
        );

        reader.IsAvailable.ShouldBe(enabled);
        result.Value.ShouldBe(
            new HotelTripReservationObservation(HotelTripReservationState.Unavailable)
        );
        store.Reads.ShouldBe(reads);
    }

    private static HotelReservation ConfirmedRow(string propertyName = FictionalName)
    {
        var terms = ReservationStateTests.Terms();
        return new()
        {
            Id = Guid.NewGuid(),
            OwnerId = Guid.NewGuid(),
            Terms = terms,
            PrivatePurgeAt = Now.AddDays(1),
            State = new()
            {
                Outcome = ReservationOutcome.Confirmed,
                Facts = new(
                    "private-booking",
                    "private-reference",
                    terms with
                    {
                        Property = terms.Property with { Name = propertyName },
                        Dates = new(new DateOnly(2027, 6, 13), new DateOnly(2027, 6, 15)),
                    }
                ),
            },
        };
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RetainedStore(HotelReservation row) : IHotelReservationStore
    {
        public Error? Failure { get; init; }
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
                Failure is { } error ? error
                : row.Id == id && row.OwnerId == owner ? row
                : null
            );
        }

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            throw new NotSupportedException("Hotel links read a single reservation.");

        public Task<ErrorOr<bool>> SaveAsync(
            HotelReservation reservation,
            int expectedVersion,
            HotelWork[] work,
            CancellationToken ct
        ) => throw new NotSupportedException("Hotel links never modify reservations.");
    }
}
