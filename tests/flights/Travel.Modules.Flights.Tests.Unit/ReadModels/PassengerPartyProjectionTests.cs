using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using JasperFx.Events;
using Marten.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Tests.Unit.ReadModels;

public sealed class PassengerPartyProjectionTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void V3_projection_copies_exact_party_envelope_owner_count_and_advances_checkpoint(
        int count
    )
    {
        var row = Row();
        var snapshot = ProtectedPassengerPartySnapshot.Create(1, "opaque-no-keys-required").Value;
        var held = new OfferHeldV3(
            "ord_party",
            snapshot,
            At.AddHours(1),
            At,
            Guid.NewGuid(),
            Guid.NewGuid(),
            count
        );
        OrderReadModelEventApplier.Apply(
            row,
            new Event<OfferHeldV3>(held) { StreamId = row.AggregateId, Version = 2 }
        );
        row.Status.ShouldBe("Held");
        row.UserId.ShouldBe(held.OwnerUserId);
        row.ProviderOrderId.ShouldBe("ord_party");
        Count(row).ShouldBe(count);
        JsonSerializer
            .Deserialize<ProtectedPassengerPartySnapshot>(row.PassengerInfoJson)
            .ShouldBe(snapshot);
        row.ProjectedStreamVersion.ShouldBe(2);
        OrderReadModelEventApplier.ShouldMaterialize(row).ShouldBeTrue();
        var payment = new PaymentAuthorized(
            Core.ValueObjects.Identifiers.PaymentRef.New(),
            Money.Create(100, CurrencyCode.Create("USD").Value).Value,
            At
        );
        OrderReadModelEventApplier.Apply(
            row,
            new Event<PaymentAuthorized>(payment) { StreamId = row.AggregateId, Version = 3 }
        );
        Count(row).ShouldBe(count);
        row.ProjectedStreamVersion.ShouldBe(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Invalid_V3_count_cannot_advance_or_materialize(int count)
    {
        var row = Row();
        var held = new OfferHeldV3(
            "ord_party",
            ProtectedPassengerPartySnapshot.Create(1, "opaque").Value,
            At.AddHours(1),
            At,
            Guid.NewGuid(),
            Guid.NewGuid(),
            count
        );
        var error = Should.Throw<BookingProjectionTerminalException>(() =>
            OrderReadModelEventApplier.Apply(
                row,
                new Event<OfferHeldV3>(held) { StreamId = row.AggregateId, Version = 2 }
            )
        );
        error.Message.ShouldBe("SourcePayloadInvalid");
        row.ProjectedStreamVersion.ShouldBe(1);
    }

    [Fact]
    public void V3_missing_revision_envelope_owner_and_conflicting_owner_fail_closed()
    {
        var valid = new OfferHeldV3(
            "ord_party",
            ProtectedPassengerPartySnapshot.Create(1, "opaque").Value,
            At.AddHours(1),
            At,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2
        );
        foreach (
            var pair in new[]
            {
                (valid with { QuoteRevision = Guid.Empty }, "SourcePayloadInvalid"),
                (valid with { PassengerSnapshot = null! }, "SourcePayloadInvalid"),
                (valid with { OwnerUserId = Guid.Empty }, "SourceOwnerMissing"),
            }
        )
        {
            var row = Row();
            Should
                .Throw<BookingProjectionTerminalException>(() =>
                    OrderReadModelEventApplier.Apply(
                        row,
                        new Event<OfferHeldV3>(pair.Item1)
                        {
                            StreamId = row.AggregateId,
                            Version = 2,
                        }
                    )
                )
                .Message.ShouldBe(pair.Item2);
            row.ProjectedStreamVersion.ShouldBe(1);
        }
        var conflict = Row();
        conflict.UserId = Guid.NewGuid();
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.Apply(
                    conflict,
                    new Event<OfferHeldV3>(valid) { StreamId = conflict.AggregateId, Version = 2 }
                )
            )
            .Message.ShouldBe("SourceOwnerConflict");
    }

    [Fact]
    public void Legacy_V1_and_V2_project_count_one_regardless_of_ticket_array()
    {
        var row = Row();
        var held = new OfferHeldV2(
            "ord_v2",
            ProtectedPassengerSnapshot.Create(1, "opaque-singular").Value,
            At.AddHours(1),
            At,
            Guid.NewGuid()
        );
        row.PassengerCount = 9;
        OrderReadModelEventApplier.Apply(
            row,
            new Event<OfferHeldV2>(held) { StreamId = row.AggregateId, Version = 2 }
        );
        Count(row).ShouldBe(1);
        row.TicketNumbers = ["a", "b", "c"];
        Count(row).ShouldBe(1);
        var legacy = new OfferHeld(
            "ord_v1",
            ValueObjects.PassengerPartyTests.Info(),
            At.AddHours(1),
            At,
            held.OwnerUserId
        );
        row.PassengerCount = 9;
        OrderReadModelEventApplier.Apply(
            row,
            new Event<OfferHeld>(legacy) { StreamId = row.AggregateId, Version = 3 }
        );
        Count(row).ShouldBe(1);
        JsonSerializer.Deserialize<PassengerInfo>(row.PassengerInfoJson).ShouldBe(legacy.Passenger);
    }

    [Fact]
    public void EF_model_has_nonnullable_count_default_one_and_one_to_nine_check_without_a_database()
    {
        using var db = Context();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(OrderReadModelEntity))!;
        var property = entity.FindProperty("PassengerCount");
        property.ShouldNotBeNull();
        property.IsNullable.ShouldBeFalse();
        property.GetDefaultValue().ShouldBe(1);
        property
            .GetColumnName(StoreObjectIdentifier.Table("order_read_model", "flights"))
            .ShouldBe("passenger_count");
        entity
            .GetCheckConstraints()
            .ShouldContain(c =>
                c.Name == "ck_order_read_model_passenger_count"
                && c.Sql == "passenger_count BETWEEN 1 AND 9"
            );
    }

    [Fact]
    public void Metadata_selector_reads_real_count_and_version_without_party_json_or_keys()
    {
        var projection =
            (Expression<Func<OrderReadModelEntity, OrderView>>)
                typeof(OrderReadModelQueries)
                    .GetField("Projection", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null)!;
        var row = new OrderReadModelEntity
        {
            AggregateId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PassengerCount = 9,
            Status = "Confirmed",
            ItineraryJson = "{}",
            Currency = "USD",
            ProviderOrderId = "ord_party",
            ProjectedStreamVersion = 123,
            PassengerInfoJson = "raw-pii-sentinel-that-must-not-be-read",
            TicketNumbers = ["only-one-ticket"],
            BookedAt = At,
        };
        var view = projection.Compile()(row);
        view.PassengerCount.ShouldBe(9);
        view.ProjectedStreamVersion.ShouldBe(123);
        using var db = Context();
        var getSql = db
            .Orders.Where(o => o.AggregateId == row.AggregateId && o.UserId == row.UserId)
            .Select(projection)
            .Take(1)
            .ToQueryString();
        var listSql = db
            .Orders.Where(o => o.UserId == row.UserId)
            .OrderByDescending(o => o.BookedAt)
            .ThenByDescending(o => o.AggregateId)
            .Skip(0)
            .Take(20)
            .Select(projection)
            .ToQueryString();
        foreach (var sql in new[] { getSql, listSql })
        {
            sql.ShouldContain("passenger_count");
            sql.ShouldContain("projected_stream_version");
            sql.ShouldNotContain("passenger_info_json");
            sql.ShouldNotContain("raw-pii-sentinel");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Corrupted_persisted_count_cannot_materialize(int count)
    {
        var row = new OrderReadModelEntity
        {
            AggregateId = Guid.NewGuid(),
            Status = "Held",
            UserId = Guid.NewGuid(),
            BookedAt = At,
            ProviderOrderId = "ord_party",
            ItineraryJson = "{}",
            PassengerInfoJson = "{}",
            PassengerCount = count,
        };
        Should
            .Throw<BookingProjectionTerminalException>(() =>
                OrderReadModelEventApplier.ShouldMaterialize(row)
            )
            .Message.ShouldBe("SourcePayloadInvalid");
    }

    private static int Count(OrderReadModelEntity row) => row.PassengerCount;

    private static OrderReadModelEntity Row() =>
        new()
        {
            AggregateId = Guid.NewGuid(),
            ProjectedStreamVersion = 1,
            ItineraryJson = "{}",
        };

    private static FlightsDbContext Context() =>
        new(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql("Host=127.0.0.1;Database=not_opened;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention()
                .Options
        );
}
