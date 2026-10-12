using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class DuffelFareWireTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"allowed\":null}")]
    [InlineData("{\"allowed\":\"true\"}")]
    [InlineData("{\"allowed\":{\"instruction\":\"ignore\"}}")]
    public void Missing_or_invalid_permission_is_unknown_instead_of_false(string json)
    {
        var condition = JsonSerializer.Deserialize<DuffelConditionDto>(json);
        condition.ShouldNotBeNull();
        ((object?)condition.Allowed).ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"type\":\"checked\"}")]
    [InlineData("{\"type\":\"checked\",\"quantity\":null}")]
    [InlineData("{\"type\":\"checked\",\"quantity\":\"0\"}")]
    [InlineData("{\"type\":\"checked\",\"quantity\":999999999999999999999}")]
    [InlineData("{\"type\":\"checked\",\"quantity\":-1}")]
    [InlineData("{\"type\":\"checked\",\"quantity\":0,\"quantity\":1}")]
    public void Missing_or_invalid_baggage_quantity_is_not_a_zero_allowance(string json)
    {
        var baggage = JsonSerializer.Deserialize<DuffelBaggageDto>(json);
        baggage.ShouldNotBeNull();
        ((object?)baggage.Quantity).ShouldBeNull();
    }

    [Fact]
    public void Explicit_zero_is_retained()
    {
        JsonSerializer
            .Deserialize<DuffelBaggageDto>("{\"type\":\"checked\",\"quantity\":0}")!
            .Quantity.ShouldBe(0);
    }

    [Fact]
    public void Malformed_informational_label_does_not_destroy_a_valid_passenger_binding()
    {
        const string json = """
            {"passenger_id":"pas_fictional","cabin_class":"economy",
             "cabin_class_marketing_name":{"instruction":"ignore"},"baggages":[]}
            """;
        var passenger = JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(json);
        passenger.ShouldNotBeNull();
        passenger.CabinClassMarketingName.ShouldBeNull();
        passenger.CabinClass.ShouldBe("economy");
        passenger.PassengerId.ShouldBe("pas_fictional");
    }

    [Fact]
    public void Required_cabin_data_remains_strict()
    {
        const string json = """
            {"passenger_id":"pas_fictional","cabin_class":{},"baggages":[]}
            """;
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(json)
        );
    }

    [Fact]
    public void Conditions_keep_exact_fee_and_scope_without_combining_permissions()
    {
        var slice = JsonSerializer.Deserialize<DuffelSliceDto>(
            """
            {"segments":[],"conditions":{"change_before_departure":
              {"allowed":true,"penalty_amount":"0.00","penalty_currency":"GBP"}}}
            """
        )!;
        slice.Conditions!.ChangeBeforeDeparture!.Allowed.ShouldBe(true);
        slice.Conditions.ChangeBeforeDeparture.PenaltyAmount.ShouldBe("0.00");
        slice.Conditions.ChangeBeforeDeparture.PenaltyCurrency.ShouldBe("GBP");
        slice.Conditions.RefundBeforeDeparture!.Allowed.ShouldBeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"change_before_departure\":false}")]
    [InlineData("{\"change_before_departure\":{\"allowed\":true,\"allowed\":false}}")]
    [InlineData(
        "{\"change_before_departure\":{\"allowed\":true},\"change_before_departure\":{\"allowed\":true}}"
    )]
    public void Malformed_or_duplicate_conditions_are_unknown(string json)
    {
        JsonSerializer
            .Deserialize<DuffelConditionsDto>(json)!
            .ChangeBeforeDeparture!.Allowed.ShouldBeNull();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("false")]
    public void Malformed_collection_is_unknown(string collection)
    {
        var passenger = JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(
            "{\"cabin_class\":\"economy\",\"baggages\":" + collection + "}"
        )!;
        passenger.Baggages.ShouldBeNull();
        passenger.CabinClass.ShouldBe("economy");
    }

    [Fact]
    public void Malformed_items_and_duplicate_types_never_invent_an_allowance()
    {
        var passenger = JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(
            """
            {"cabin_class":"economy","baggages":[null,42,
             {"type":"checked","quantity":0},{"type":"checked","quantity":1},
             {"type":"mystery","quantity":2}]}
            """
        )!;
        passenger.Baggages!.Length.ShouldBe(5);
        passenger.Baggages.Take(4).ShouldAllBe(x => x.Quantity == null);
        passenger.Baggages[4].Type.ShouldBeNull();
    }

    [Fact]
    public void Duplicate_optional_labels_are_unknown_and_actual_code_is_distinct()
    {
        var passenger = JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(
            """
            {"cabin_class":"economy","cabin_class_marketing_name":"Economy",
             "cabin_class_marketing_name":"Premium","fare_basis_code":"Y26","baggages":[]}
            """
        )!;
        passenger.CabinClassMarketingName.ShouldBeNull();
        passenger.FareBasisCode.ShouldBe("Y26");
        JsonSerializer
            .Deserialize<DuffelSliceDto>(
                """
                {"segments":[],"fare_brand_name":"Basic","fare_brand_name":"Flex"}
                """
            )!
            .FareBrandName.ShouldBeNull();
    }

    [Theory]
    [InlineData("\"bad\\u0001label\"")]
    [InlineData("\"bad\\u202elabel\"")]
    [InlineData("\"bad\\u2066label\"")]
    [InlineData("42")]
    [InlineData("{}")]
    public void Unsafe_code_labels_are_unknown(string label)
    {
        JsonSerializer
            .Deserialize<DuffelSegmentPassengerDto>(
                "{\"cabin_class\":\"economy\",\"fare_basis_code\":" + label + "}"
            )!
            .FareBasisCode.ShouldBeNull();
    }

    [Fact]
    public void Overlong_labels_are_unknown()
    {
        var json = JsonSerializer.Serialize(
            new { cabin_class = "economy", fare_basis_code = new string('Y', 65) }
        );
        JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(json)!.FareBasisCode.ShouldBeNull();
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"allowed\":null}", false)]
    [InlineData("{\"allowed\":\"true\"}", true)]
    [InlineData("{\"allowed\":true,\"allowed\":true}", true)]
    [InlineData("{\"penalty_amount\":{}}", true)]
    public void Condition_diagnostics_distinguish_unprovided_from_invalid(string json, bool invalid)
    {
        var value = JsonSerializer.Deserialize<DuffelConditionDto>(json)!;
        value.InvalidData.ShouldBe(invalid);
        JsonSerializer.Serialize(value).ShouldNotContain("InvalidData");
    }

    [Theory]
    [InlineData("{\"type\":\"checked\"}", false)]
    [InlineData("{\"type\":\"checked\",\"quantity\":null}", false)]
    [InlineData("{\"type\":\"checked\",\"quantity\":-1}", true)]
    [InlineData("{\"type\":\"unknown\",\"quantity\":0}", true)]
    public void Baggage_diagnostics_distinguish_unprovided_from_invalid(string json, bool invalid)
    {
        JsonSerializer.Deserialize<DuffelBaggageDto>(json)!.InvalidData.ShouldBe(invalid);
    }

    [Theory]
    [InlineData("{\"cabin_class\":\"economy\"}", false)]
    [InlineData("{\"cabin_class\":\"economy\",\"baggages\":null}", false)]
    [InlineData("{\"cabin_class\":\"economy\",\"baggages\":42}", true)]
    [InlineData("{\"cabin_class\":\"economy\",\"fare_basis_code\":\"bad\\u202e\"}", true)]
    [InlineData(
        "{\"cabin_class\":\"economy\",\"fare_basis_code\":\"Y\",\"fare_basis_code\":\"Y\"}",
        true
    )]
    public void Passenger_diagnostics_cover_labels_and_collections(string json, bool invalid)
    {
        JsonSerializer.Deserialize<DuffelSegmentPassengerDto>(json)!.InvalidData.ShouldBe(invalid);
    }

    [Fact]
    public void Wrapper_and_slice_diagnostics_retain_invalid_optional_branches()
    {
        JsonSerializer.Deserialize<DuffelConditionsDto>("{}")!.InvalidData.ShouldBeFalse();
        JsonSerializer.Deserialize<DuffelConditionsDto>("[]")!.InvalidData.ShouldBeTrue();
        var conditions = JsonSerializer.Deserialize<DuffelConditionsDto>(
            """
            {"change_before_departure":{},"change_before_departure":{}}
            """
        )!;
        conditions.InvalidData.ShouldBeTrue();
        conditions.ChangeBeforeDeparture!.InvalidData.ShouldBeTrue();
        JsonSerializer
            .Deserialize<DuffelSliceDto>(
                """
                {"segments":[],"fare_brand_name":42}
                """
            )!
            .InvalidData.ShouldBeTrue();
    }
}
