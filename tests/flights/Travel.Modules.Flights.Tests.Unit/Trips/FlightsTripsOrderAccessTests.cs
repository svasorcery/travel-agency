using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Trips;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Unit.Booking;
using Travel.Modules.Flights.Tests.Unit.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Trips;

public sealed class FlightsTripsOrderAccessTests
{
    private static readonly DateTimeOffset Now = RefundTestBookings.Now;
    private static readonly Guid Owner = RefundTestBookings.Owner;
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static FlightOrderObservation Observe(BookingAggregate b, DateTimeOffset? now = null) =>
        FlightsTripsOrderAccess.Observe(b.OwnerUserId!.Value, b.Id, b, 7, now ?? Now).Value;

    [Fact]
    public void Only_owner_consistent_materialized_source_with_a_positive_version_proves_access()
    {
        var b = RefundTestBookings.Held();
        var quote = BookingCreationTests.Quoted();
        FlightsTripsOrderAccess
            .Observe(Owner, b.Id, null, 1, Now)
            .FirstError.Type.ShouldBe(ErrorType.NotFound);
        FlightsTripsOrderAccess
            .Observe(Owner, quote.Id, quote, 1, Now)
            .FirstError.Type.ShouldBe(ErrorType.NotFound);
        FlightsTripsOrderAccess
            .Observe(Owner, Guid.NewGuid(), b, 1, Now)
            .FirstError.Type.ShouldBe(ErrorType.NotFound);
        FlightsTripsOrderAccess
            .Observe(Guid.NewGuid(), b.Id, b, 1, Now)
            .FirstError.Type.ShouldBe(ErrorType.NotFound);
        FlightsTripsOrderAccess
            .Observe(Owner, b.Id, b, 0, Now)
            .FirstError.Type.ShouldBe(ErrorType.Failure);
        FlightsTripsOrderAccess
            .Observe(Owner, b.Id, b, null, Now)
            .FirstError.Type.ShouldBe(ErrorType.Failure);
        b.Apply(
            new SupplierCancellationReported(Guid.NewGuid(), Guid.NewGuid(), "test-order", Now)
        );
        FlightsTripsOrderAccess
            .Observe(Owner, b.Id, b, 1, Now)
            .FirstError.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Source_preserves_legacy_offsets_all_legs_and_explicit_party_count()
    {
        var b = RefundTestBookings.Held(passengers: 7, legs: 4);
        var slices = b.Itinerary!.Slices.Select(
                (s, i) =>
                    Slice
                        .Create(
                            s.Segments.Select(seg =>
                                    Segment
                                        .Create(
                                            seg.Origin,
                                            seg.Destination,
                                            seg.DepartAt.ToOffset(TimeSpan.FromHours(i + 1)),
                                            seg.ArriveAt.ToOffset(TimeSpan.FromHours(-i - 1)),
                                            seg.CarrierCode,
                                            seg.FlightNumber,
                                            seg.Cabin
                                        )
                                        .Value
                                )
                                .ToArray()
                        )
                        .Value
            )
            .ToArray();
        typeof(BookingAggregate)
            .GetProperty(nameof(b.Itinerary))!
            .SetValue(b, Itinerary.Create(slices).Value);
        var obs = Observe(b);
        obs.ReviewRequired.ShouldBeFalse();
        obs.StreamVersion.ShouldBe(7);
        obs.ObservedAt.ShouldBe(Now);
        obs.PassengerCount.ShouldBe(7);
        obs.Route!.Count.ShouldBe(4);
        for (var i = 0; i < slices.Length; i++)
        {
            var leg = obs.Route[i];
            leg.DepartAt.EqualsExact(slices[i].DepartAt).ShouldBeTrue();
            leg.ArriveAt.EqualsExact(slices[i].ArriveAt).ShouldBeTrue();
            leg.Origin.ShouldBe(slices[i].Origin.Value);
            leg.Destination.ShouldBe(slices[i].Destination.Value);
        }
        var tooMany = JsonSerializer.SerializeToNode(b.Itinerary)!;
        tooMany["Slices"]!.AsArray().Add(tooMany["Slices"]![0]!.DeepClone());
        typeof(BookingAggregate)
            .GetProperty(nameof(b.Itinerary))!
            .SetValue(b, tooMany.Deserialize<Itinerary>());
        Observe(b).Route.ShouldBeNull();
    }

    [Fact]
    public void Expected_expiry_and_unresolved_order_operations_each_require_review()
    {
        var b = RefundTestBookings.Held();
        Observe(b, b.ExpiresAt).ReviewRequired.ShouldBeTrue();
        typeof(BookingAggregate).GetProperty(nameof(b.ExpiresAt))!.SetValue(b, null);
        Observe(b).ReviewRequired.ShouldBeTrue();
        b = RefundTestBookings.Held();
        b.Apply(
            new ConfirmationAttemptStarted(
                Guid.NewGuid(),
                Owner,
                Guid.NewGuid(),
                new string('a', 64),
                RefundTestBookings.Order,
                b.TotalAmount!,
                Now
            )
        );
        Observe(b).ReviewRequired.ShouldBeTrue();
        b = RefundTestBookings.Held();
        b.Apply(
            new CancellationPreparationStarted(
                Guid.NewGuid(),
                Owner,
                RefundTestBookings.Order,
                new string('a', 64),
                new string('b', 64),
                Guid.NewGuid(),
                Now
            )
        );
        Observe(b).ReviewRequired.ShouldBeTrue();
        b = RefundTestBookings.Held();
        b.Apply(
            new SupplierCancellationReported(Guid.NewGuid(), Owner, RefundTestBookings.Order, Now)
        );
        Observe(b).ReviewRequired.ShouldBeTrue();
    }

    [Fact]
    public void Materialized_creation_difference_clears_only_after_closure()
    {
        var b = BookingCreationTests.Quoted();
        var start = BookingCreationTests.Start(b).Value.Event!;
        b.Apply(start);
        var actual = BookingCreationTests.Actual(b) with { Total = RefundTestBookings.Money(60) };
        var observed = b.DecideCreationObservation(
            start.AttemptId,
            1,
            new(
                BookingCreationOutcome.CreatedWithDifferences,
                actual,
                actual.ProviderOrderId,
                true,
                true,
                "ServicesChanged",
                start.OccurredAt
            ),
            start.OccurredAt
        ).Value.Event!;
        b.Apply(
            new OfferHeldV3(
                actual.ProviderOrderId,
                start.ProtectedParty,
                actual.PaymentRequiredBy,
                start.OccurredAt,
                start.OwnerId,
                start.QuoteRevision,
                1
            )
        );
        b.HasUnresolvedCreation.ShouldBeTrue();
        Observe(b, start.OccurredAt).ReviewRequired.ShouldBeTrue();
        b.Apply(observed);
        b.HasUnresolvedCreation.ShouldBeFalse();
        b.CreationBlocksConfirmation.ShouldBeTrue();
        Observe(b, start.OccurredAt).ReviewRequired.ShouldBeTrue();
        b.Apply(new OrderCancelled(CancelReason.User, start.OccurredAt));
        Observe(b).BookingStatus.ShouldBe(FlightOrderBookingStatus.Cancelled);
        Observe(b).ReviewRequired.ShouldBeFalse();
        b.Apply(new OrderRefunded(RefundRef.New(), b.TotalAmount!, RefundInitiator.Airline, Now));
        Observe(b).BookingStatus.ShouldBe(FlightOrderBookingStatus.Refunded);
        Observe(b).ReviewRequired.ShouldBeFalse();
    }

    [Fact]
    public void Independent_customer_refund_manual_review_is_outside_order_review()
    {
        var b = CustomerRefundDecisionTests.Claimed();
        var refund = b.CustomerRefund!;
        CustomerRefundDecisionTests.Apply(
            b,
            b.DecideCustomerRefundDeadline(refund.Id, refund.DeadlineAt, refund.DeadlineAt)
        );
        b.CustomerRefund!.NeedsManualReview.ShouldBeTrue();
        Observe(b).ReviewRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task Candidate_list_is_projection_only_and_preserves_unavailable_routes()
    {
        var b = RefundTestBookings.Held(2, 4);
        var query = new CandidateQueries();
        var port = new FlightsTripsOrderAccess(null!, query, TimeProvider.System);
        (await port.ListCandidatesAsync(Owner, 0, Ct)).Value.Items.ShouldBeEmpty();
        query.Rows = [Row(JsonSerializer.Serialize(b.Itinerary)), Row("{"), Row("{}")];
        var page = (await port.ListCandidatesAsync(Owner, 0, Ct)).Value;
        page.Items.Count.ShouldBe(3);
        page.Items[0].Route!.Count.ShouldBe(4);
        page.Items[1].Route.ShouldBeNull();
        page.Items[2].Route.ShouldBeNull();
        page.NextOffset.ShouldBeNull();
        query.Rows = [Row(JsonSerializer.Serialize(b.Itinerary).Replace("+00:00", ""))];
        (await port.ListCandidatesAsync(Owner, 0, Ct)).Value.Items.Single().Route.ShouldBeNull();
        query.Rows = [Row("{}") with { OwnerId = Guid.NewGuid() }];
        (await port.ListCandidatesAsync(Owner, 0, Ct)).FirstError.Type.ShouldBe(ErrorType.Failure);
        query.Rows = [Row("{}") with { OrderId = Guid.Empty }];
        (await port.ListCandidatesAsync(Owner, 0, Ct)).FirstError.Type.ShouldBe(ErrorType.Failure);
        query.Rows = [Row("{}") with { Status = "1" }];
        (await port.ListCandidatesAsync(Owner, 0, Ct)).FirstError.Type.ShouldBe(ErrorType.Failure);
        query.Fault = true;
        (await port.ListCandidatesAsync(Owner, 0, Ct)).FirstError.Type.ShouldBe(ErrorType.Failure);
    }

    [Fact]
    public async Task Oversized_projected_route_preserves_one_candidate_without_parsing_the_payload()
    {
        var json = JsonSerializer.Serialize(RefundTestBookings.Held().Itinerary);
        var oversized = json.Insert(
            1,
            "\"historicalExtra\":\"" + new string('я', 33 * 1024) + "\","
        );
        oversized.Length.ShouldBeLessThan(ProjectedFlightOrderCandidate.MaximumItineraryBytes);
        var row = Row(oversized);
        var port = new FlightsTripsOrderAccess(
            null!,
            new CandidateQueries { Rows = [row] },
            TimeProvider.System
        );
        var candidate = (await port.ListCandidatesAsync(Owner, 0, Ct)).Value.Items.Single();
        candidate.OrderId.ShouldBe(row.OrderId);
        candidate.Route.ShouldBeNull();
    }

    [Fact]
    public void Candidate_SQL_bounds_json_on_the_server_and_fetches_only_the_projected_fields()
    {
        using var db = new FlightsDbContextFactory().CreateDbContext([]);
        var sql = new OrderReadModelQueries(db)
            .CandidateQuery(Owner, 21, 0)
            .ToQueryString()
            .ToLowerInvariant();
        sql.ShouldContain("octet_length(itinerary_json::text)");
        sql.ShouldContain("'null'::jsonb");
        sql.ShouldContain("where user_id =");
        sql.ShouldContain("order by");
        sql.ShouldContain("limit");
        foreach (
            var privateColumn in new[]
            {
                "ticket_numbers",
                "provider_order_id",
                "passenger_info_json",
                "total_amount",
                "currency",
            }
        )
            sql.ShouldNotContain(privateColumn);
    }

    [Theory]
    [InlineData(0, 20, false)]
    [InlineData(9980, 10000, false)]
    [InlineData(9981, null, true)]
    [InlineData(10000, null, true)]
    public async Task Candidate_lookahead_respects_last_allowed_offset(
        int offset,
        int? next,
        bool bound
    )
    {
        var rows = Enumerable.Range(0, 21).Select(_ => Row("{}")).ToArray();
        var query = new CandidateQueries { Rows = rows };
        var port = new FlightsTripsOrderAccess(null!, query, TimeProvider.System);
        var page = (await port.ListCandidatesAsync(Owner, offset, Ct)).Value;
        page.Items.Select(i => i.OrderId).ShouldBe(rows.Take(20).Select(i => i.OrderId));
        page.NextOffset.ShouldBe(next);
        page.PageBoundReached.ShouldBe(bound);
        query.Last.ShouldBe((Owner, 21, offset));
        (await port.ListCandidatesAsync(Owner, -1, Ct)).FirstError.Type.ShouldBe(
            ErrorType.Validation
        );
        (await port.ListCandidatesAsync(Owner, 10001, Ct)).FirstError.Type.ShouldBe(
            ErrorType.Validation
        );
        query.Calls.ShouldBe(1);
    }

    private static ProjectedFlightOrderCandidate Row(string json) =>
        new(Guid.NewGuid(), Owner, "Held", 2, json);

    private sealed class CandidateQueries : IOrderReadModelQueries
    {
        public IReadOnlyList<ProjectedFlightOrderCandidate> Rows = [];
        public bool Fault;
        public int Calls;
        public (Guid, int, int) Last;

        public Task<IReadOnlyList<ProjectedFlightOrderCandidate>> ListCandidatesAsync(
            Guid ownerId,
            int limit,
            int offset,
            CancellationToken ct
        )
        {
            Calls++;
            Last = (ownerId, limit, offset);
            return Fault
                ? throw new IOException("Fictional projection unavailable")
                : Task.FromResult(Rows);
        }

        public Task<OrderListView> ListAsync(
            Guid userId,
            int limit,
            int offset,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<OrderView?> GetAsync(Guid aggregateId, Guid userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
