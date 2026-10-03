using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelPartyMappingTests
{
    private static JsonObject Offer(int count = 2)
    {
        var root = JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Providers",
                    "Duffel",
                    "Fixtures",
                    "offer-oneway.json"
                )
            )
        )!["data"]!["offers"]![0]!.AsObject();
        root["passengers"] = new JsonArray(
            Enumerable
                .Range(1, count)
                .Select(i => (JsonNode)new JsonObject { ["id"] = $"pas_{i}", ["type"] = "adult" })
                .ToArray()
        );
        root["payment_requirements"] = new JsonObject { ["requires_instant_payment"] = false };
        root["passenger_identity_documents_required"] = false;
        foreach (var s in root["slices"]!.AsArray())
        foreach (var seg in s!["segments"]!.AsArray())
            seg!["passengers"] = new JsonArray(
                Enumerable
                    .Range(1, count)
                    .Select(i =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["passenger_id"] = $"pas_{i}",
                                ["cabin_class"] = "economy",
                            }
                    )
                    .ToArray()
            );
        return root;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public void Maps_exact_adult_party(int count)
    {
        var result = DuffelOfferMapper.Map(
            Offer(count).Deserialize<DuffelOfferDto>()!,
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        result.Value.Party.ShouldNotBeNull();
        result.Value.Party!.PassengerCount.ShouldBe(count);
    }

    [Theory]
    [InlineData("2027-02-10T00:30:00+14:00")]
    [InlineData("2027-02-10T00:30:00")]
    public void Keeps_origin_local_date_from_wire_before_utc(string departing)
    {
        var dto = Offer();
        var seg = dto["slices"]![0]!["segments"]![0]!;
        seg["origin"]!["time_zone"] = "Pacific/Kiritimati";
        seg["destination"]!["time_zone"] = "Pacific/Kiritimati";
        seg["departing_at"] = departing;
        seg["arriving_at"] = departing.Contains('+')
            ? "2027-02-10T02:30:00+14:00"
            : "2027-02-10T02:30:00";
        var result = DuffelOfferMapper.Map(
            dto.Deserialize<DuffelOfferDto>()!,
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        result.Value.Party.ShouldNotBeNull();
        result.Value.Party!.FirstDepartureLocalDate.ShouldBe(new DateOnly(2027, 2, 10));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("child")]
    [InlineData("cabin")]
    public void Rejects_malformed_party(string kind)
    {
        var dto = Offer();
        if (kind == "duplicate")
            dto["passengers"]![1]!["id"] = "pas_1";
        if (kind == "missing")
            dto.Remove("passengers");
        if (kind == "child")
            dto["passengers"]![0]!["type"] = "child";
        if (kind == "cabin")
            dto["slices"]![0]!["segments"]![0]!["passengers"]![1]!["cabin_class"] = "business";
        DuffelOfferMapper
            .Map(dto.Deserialize<DuffelOfferDto>()!, new FakeTimeProvider())
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_capabilities_remain_unknown()
    {
        var dto = Offer();
        dto.Remove("payment_requirements");
        dto.Remove("passenger_identity_documents_required");
        var result = DuffelOfferMapper.Map(
            dto.Deserialize<DuffelOfferDto>()!,
            new FakeTimeProvider()
        );
        result.IsError.ShouldBeFalse();
        result.Value.Party!.SupportsHold.ShouldBeNull();
        result.Value.Party.RequiresIdentityDocuments.ShouldBeNull();
    }

    [Fact]
    public void Rejects_foreign_segment_passenger()
    {
        var dto = Offer();
        dto["slices"]![0]!["segments"]![0]!["passengers"]![0]!["passenger_id"] = "foreign";
        DuffelOfferMapper
            .Map(dto.Deserialize<DuffelOfferDto>()!, new FakeTimeProvider())
            .IsError.ShouldBeTrue();
    }
}
