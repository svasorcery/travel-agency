using System.Globalization;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelComparisonTests
{
    [Fact]
    public void Private_reload_keeps_original_safe_source_without_transport_account_or_context()
    {
        var row = new Travel.Modules.Hotels.Application.HotelReservation
        {
            Id = Guid.NewGuid(),
            Provider = "hotelbeds",
            Environment = "Test",
            ProviderAccount = "private-transport-account",
            ProtectedContext = "private-context",
            Terms = ReservationStateTests.Terms(),
        };
        row.State.AdmissionBinding = new(
            2,
            new("hotelbeds", "safe-alias", "Test", "Fictional"),
            "r1",
            "Net",
            "owned",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(1),
            "private-transport-account"
        );
        row.State.AcceptedConditions = new(
            "SourcePolicies",
            [],
            [
                new(
                    DateTimeOffset.Parse("2026-11-01T12:00:00+01:00", CultureInfo.InvariantCulture),
                    new(200, "GBP"),
                    "WholeStay",
                    "hotelbeds",
                    "Penalty",
                    "From"
                ),
            ],
            "hotelbeds"
        );
        var json = JsonSerializer.Serialize(
            Travel.Modules.Hotels.Application.HotelReservationView.From(row, DateTimeOffset.UtcNow)
        );
        using var parsed = JsonDocument.Parse(json);
        parsed.RootElement.TryGetProperty("Provider", out var provider).ShouldBeTrue();
        provider.GetString().ShouldBe("hotelbeds");
        parsed
            .RootElement.GetProperty("AcceptedConditions")
            .GetProperty("Cancellation")[0]
            .GetProperty("AmountKind")
            .GetString()
            .ShouldBe("Penalty");
        json.ShouldContain("safe-alias");
        json.ShouldNotContain("private-transport-account");
        json.ShouldNotContain("private-context");
    }

    [Theory]
    [InlineData(ExecutionKind.Complete, ExecutionKind.Complete, ExecutionKind.Complete)]
    [InlineData(ExecutionKind.Complete, ExecutionKind.Failed, ExecutionKind.Limited)]
    [InlineData(ExecutionKind.Limited, ExecutionKind.Failed, ExecutionKind.Limited)]
    [InlineData(ExecutionKind.Failed, ExecutionKind.Unsupported, ExecutionKind.Failed)]
    [InlineData(ExecutionKind.Unsupported, ExecutionKind.Unsupported, ExecutionKind.Unsupported)]
    public void Aggregate_truth_does_not_depend_on_inventory_count(
        ExecutionKind a,
        ExecutionKind b,
        ExecutionKind expected
    )
    {
        HotelAggregation.Kind([new("a", a, null), new("b", b, null)]).ShouldBe(expected);
    }

    [Fact]
    public void Included_tax_is_not_added_and_whole_stay_is_not_multiplied()
    {
        var price = Price([new("Excluded", new(10, "GBP"), "WholeStay")]);
        price.ComparableTotal().ShouldBe(new StayMoney(210, "GBP"));
        price.SupplierPayable.Amount.ShouldBe(200);
        price.TravellerPrice.ShouldBeNull();
    }

    [Fact]
    public void Missing_and_foreign_obligations_are_not_zero()
    {
        Price(null).ComparableTotal().ShouldBeNull();
        Price([new("Excluded", new(20, "EUR"), "WholeStay")]).ComparableTotal().ShouldBeNull();
        Price([new("Unknown", null, "WholeStay")]).ComparableTotal().ShouldBeNull();
        Price([]).ComparableTotal().ShouldBe(new StayMoney(200, "GBP"));
    }

    [Fact]
    public void Namespace_and_binding_are_durable_outside_accepted_terms()
    {
        var now = DateTimeOffset.Parse("2026-10-19T12:00:00Z", CultureInfo.InvariantCulture);
        var source = new HotelSourceIdentity(
            "hotelbeds",
            "public-account-alias",
            "Test",
            "Fictional"
        );
        var binding = new HotelAdmissionBinding(
            2,
            source,
            "config-1",
            "Net",
            "approval-1",
            "pair-1",
            "map-1",
            now.AddMinutes(5)
        );
        var state = new ReservationState { AdmissionBinding = binding, QuoteContractVersion = 2 };
        var restored = JsonSerializer.Deserialize<ReservationState>(
            JsonSerializer.Serialize(state)
        )!;
        restored.AdmissionBinding.ShouldBe(binding);
        restored.QuoteContractVersion.ShouldBe(2);
        JsonSerializer.Deserialize<ReservationState>("{}")!.AdmissionBinding.ShouldBeNull();
        (source with { Account = "other" }).ShouldNotBe(source);
        (source with { Provider = "duffel-stays" }).ShouldNotBe(source);
    }

    private static HotelPriceEvidence Price(HotelObligation[]? obligations) =>
        new(
            "SupplierNet",
            new(200, "GBP"),
            null,
            "WholeStay",
            "source",
            [new("Included", new(20, "GBP"), "WholeStay")],
            obligations
        );
}
