using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelAirportTimeResolverTests
{
    [Theory]
    [InlineData("2030-06-01T10:00:00", "Europe/London", 1)]
    [InlineData("2030-06-01T10:00:00", "America/New_York", -4)]
    [InlineData("2030-01-01T10:00:00", "Europe/London", 0)]
    [InlineData("2030-01-01T10:00:00Z", "Europe/London", 0)]
    [InlineData("2030-06-01T10:00:00.1234567+01:00", "Europe/London", 1)]
    public void Resolver_uses_airport_rules_for_the_local_date(string raw, string zoneId, int hours)
    {
        var result = DuffelAirportTimeResolver.Resolve(raw, zoneId);
        result.IsError.ShouldBeFalse();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        result.Value.Offset.ShouldBe(zone.GetUtcOffset(result.Value));
        result.Value.Offset.ShouldBe(TimeSpan.FromHours(hours));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2030-02-30T10:00:00")]
    [InlineData("2030-06-01T25:00:00")]
    [InlineData(" 2030-06-01T10:00:00")]
    [InlineData("2030-06-01T10:00:00 ")]
    [InlineData("2030-06-01T10:00:00\n")]
    [InlineData("2030-06-01T10:00:00sensitive@example.test")]
    [InlineData("2030-06-01 10:00:00")]
    [InlineData("2030-06-01T10:00")]
    [InlineData("2030-06-01T10:00:00.12345678")]
    [InlineData("2030-06-01T10:00:00+0100")]
    [InlineData("2030-06-01T10:00:00+01:60")]
    [InlineData("2030-06-01T10:00:00+15:00")]
    public void Resolver_rejects_malformed_text_without_copying_it_into_errors(string? raw)
    {
        var result = DuffelAirportTimeResolver.Resolve(raw!, "Europe/London");
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("DuffelOffer.InvalidAirportTime");
        result.FirstError.Description.ShouldBe("Supplier airport time is invalid or unresolved.");
    }

    [Theory]
    [InlineData("2030-03-31T01:30:00", "Europe/London")]
    [InlineData("2030-03-31T01:30:00+00:00", "Europe/London")]
    [InlineData("2030-03-31T01:30:00+01:00", "Europe/London")]
    [InlineData("2030-03-10T02:30:00", "America/New_York")]
    [InlineData("2030-11-03T01:30:00", "America/New_York")]
    [InlineData("2030-10-27T01:30:00+02:00", "Europe/London")]
    public void Resolver_rejects_gaps_and_unresolved_or_conflicting_overlap_offsets(
        string raw,
        string zone
    )
    {
        DuffelAirportTimeResolver.Resolve(raw, zone).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2030-10-27T01:30:00+01:00", "Europe/London", 1)]
    [InlineData("2030-10-27T01:30:00+00:00", "Europe/London", 0)]
    [InlineData("2030-11-03T01:30:00-04:00", "America/New_York", -4)]
    [InlineData("2030-11-03T01:30:00-05:00", "America/New_York", -5)]
    public void Resolver_accepts_either_explicit_valid_overlap_offset(
        string raw,
        string zoneId,
        int hours
    )
    {
        var result = DuffelAirportTimeResolver.Resolve(raw, zoneId);
        result.IsError.ShouldBeFalse();
        result.Value.Offset.ShouldBe(TimeSpan.FromHours(hours));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        zone.GetAmbiguousTimeOffsets(result.Value.DateTime).ShouldContain(result.Value.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" Europe/London")]
    [InlineData("Europe/London ")]
    [InlineData("GMT Standard Time")]
    [InlineData("Unknown/Zone")]
    public void Resolver_never_substitutes_a_host_or_utc_zone(string? zone)
    {
        DuffelAirportTimeResolver.Resolve("2030-01-01T10:00:00Z", zone).IsError.ShouldBeTrue();
    }

    private static DuffelOfferDto Offer(
        string departure,
        string arrival,
        string? originZone = "Europe/London",
        string? destinationZone = "America/New_York"
    ) =>
        JsonSerializer.Deserialize<DuffelOfferDto>(
            JsonSerializer.Serialize(
                new
                {
                    id = "off_fictional",
                    total_amount = "100.00",
                    total_currency = "USD",
                    expires_at = "2030-01-01T12:00:00Z",
                    passengers = new[] { new { id = "pas_1", type = "adult" } },
                    slices = new[]
                    {
                        new
                        {
                            segments = new[]
                            {
                                new
                                {
                                    origin = new { iata_code = "LHR", time_zone = originZone },
                                    destination = new
                                    {
                                        iata_code = "JFK",
                                        time_zone = destinationZone,
                                    },
                                    departing_at = departure,
                                    arriving_at = arrival,
                                    marketing_carrier = new { iata_code = "ZZ" },
                                    marketing_carrier_flight_number = "101",
                                    passengers = new[]
                                    {
                                        new { passenger_id = "pas_1", cabin_class = "economy" },
                                    },
                                },
                            },
                        },
                    },
                }
            )
        )!;

    [Fact]
    public void Airport_local_clocks_can_arrive_earlier_while_the_absolute_instant_is_later()
    {
        var result = DuffelOfferMapper.Map(
            Offer("2030-06-01T10:00:00", "2030-06-01T09:30:00"),
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        var segment = result.Value.Itinerary.Slices[0].Segments[0];
        segment.DepartAt.Offset.ShouldBe(TimeSpan.FromHours(1));
        segment.ArriveAt.Offset.ShouldBe(TimeSpan.FromHours(-4));
        segment.Duration.ShouldBe(TimeSpan.FromHours(4.5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown/Zone")]
    [InlineData("GMT Standard Time")]
    public void Missing_or_unresolvable_iana_zone_is_rejected_even_with_an_offset(string? zone)
    {
        DuffelOfferMapper
            .Map(
                Offer("2030-06-01T10:00:00+01:00", "2030-06-01T12:00:00-04:00", zone),
                new FakeTimeProvider()
            )
            .IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unknown/Zone")]
    [InlineData("Eastern Standard Time")]
    public void Arrival_requires_its_own_resolvable_destination_airport_zone(string? zone)
    {
        var result = DuffelOfferMapper.Map(
            Offer("2030-06-01T10:00:00+01:00", "2030-06-01T12:00:00-04:00", destinationZone: zone),
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("DuffelOffer.InvalidAirportTime");
    }

    [Theory]
    [InlineData("2030-03-31T01:30:00", "2030-03-31T12:00:00-04:00")]
    [InlineData("2030-10-27T01:30:00", "2030-10-27T12:00:00-04:00")]
    [InlineData("2030-06-01T10:00:00Z", "2030-06-01T12:00:00-04:00")]
    public void Gaps_ambiguous_wall_time_and_conflicting_offset_are_rejected(
        string departure,
        string arrival
    )
    {
        DuffelOfferMapper
            .Map(Offer(departure, arrival), new FakeTimeProvider())
            .IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2030-10-27T01:30:00+01:00", 1, 0)]
    [InlineData("2030-10-27T01:30:00+00:00", 0, 1)]
    public void Both_explicit_valid_overlap_offsets_are_preserved(
        string departure,
        int offset,
        int utcHour
    )
    {
        var result = DuffelOfferMapper.Map(
            Offer(departure, "2030-10-27T12:00:00-04:00"),
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        var departAt = result.Value.Itinerary.Slices[0].DepartAt;
        departAt.Offset.ShouldBe(TimeSpan.FromHours(offset));
        departAt.UtcDateTime.Hour.ShouldBe(utcHour);
    }

    [Fact]
    public void First_party_date_uses_resolved_origin_local_date_instead_of_utc_date()
    {
        var result = DuffelOfferMapper.Map(
            Offer(
                "2030-06-01T00:30:00",
                "2030-06-01T02:30:00",
                "Pacific/Kiritimati",
                "Pacific/Kiritimati"
            ),
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        result.Value.Party!.FirstDepartureLocalDate.ShouldBe(new DateOnly(2030, 6, 1));
        result
            .Value.Itinerary.Slices[0]
            .DepartAt.UtcDateTime.Date.ShouldBe(new DateTime(2030, 5, 31));
    }
}
