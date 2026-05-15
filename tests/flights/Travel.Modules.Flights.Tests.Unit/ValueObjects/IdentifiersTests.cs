using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.ValueObjects;

public sealed class IdentifiersTests
{
    // OfferId
    [Fact]
    public void OfferId_New_produces_distinct_values()
    {
        OfferId.New().ShouldNotBe(OfferId.New());
    }

    [Fact]
    public void OfferId_structural_equality_holds()
    {
        var id = Guid.NewGuid();
        new OfferId(id).ShouldBe(new OfferId(id));
    }

    // OrderId
    [Fact]
    public void OrderId_New_produces_distinct_values()
    {
        OrderId.New().ShouldNotBe(OrderId.New());
    }

    [Fact]
    public void OrderId_structural_equality_holds()
    {
        var id = Guid.NewGuid();
        new OrderId(id).ShouldBe(new OrderId(id));
    }

    // PaymentRef
    [Fact]
    public void PaymentRef_New_produces_distinct_values()
    {
        PaymentRef.New().ShouldNotBe(PaymentRef.New());
    }

    [Fact]
    public void PaymentRef_structural_equality_holds()
    {
        var id = Guid.NewGuid();
        new PaymentRef(id).ShouldBe(new PaymentRef(id));
    }

    // RefundRef
    [Fact]
    public void RefundRef_New_produces_distinct_values()
    {
        RefundRef.New().ShouldNotBe(RefundRef.New());
    }

    [Fact]
    public void RefundRef_structural_equality_holds()
    {
        var id = Guid.NewGuid();
        new RefundRef(id).ShouldBe(new RefundRef(id));
    }

    // AggregateId
    [Fact]
    public void AggregateId_New_produces_distinct_values()
    {
        AggregateId.New().ShouldNotBe(AggregateId.New());
    }

    [Fact]
    public void AggregateId_structural_equality_holds()
    {
        var id = Guid.NewGuid();
        new AggregateId(id).ShouldBe(new AggregateId(id));
    }

    // ProviderId
    [Fact]
    public void ProviderId_Duffel_equals_new_ProviderId_with_same_value()
    {
        ProviderId.Duffel.ShouldBe(new ProviderId("duffel"));
    }

    [Fact]
    public void ProviderId_Travelpayouts_equals_new_ProviderId_with_same_value()
    {
        ProviderId.Travelpayouts.ShouldBe(new ProviderId("travelpayouts"));
    }

    [Fact]
    public void ProviderId_structural_equality_holds()
    {
        new ProviderId("duffel").ShouldBe(new ProviderId("duffel"));
    }

    // ── IsEmpty / None guards ─────────────────────────────────────────────────

    [Fact]
    public void Identifier_default_is_empty()
    {
        default(OfferId).IsEmpty.ShouldBeTrue();
        default(OrderId).IsEmpty.ShouldBeTrue();
        default(PaymentRef).IsEmpty.ShouldBeTrue();
        default(RefundRef).IsEmpty.ShouldBeTrue();
        default(AggregateId).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Identifier_new_is_not_empty()
    {
        OfferId.New().IsEmpty.ShouldBeFalse();
        OrderId.New().IsEmpty.ShouldBeFalse();
        PaymentRef.New().IsEmpty.ShouldBeFalse();
        RefundRef.New().IsEmpty.ShouldBeFalse();
        AggregateId.New().IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Identifier_None_is_empty()
    {
        OfferId.None.IsEmpty.ShouldBeTrue();
        OrderId.None.IsEmpty.ShouldBeTrue();
        PaymentRef.None.IsEmpty.ShouldBeTrue();
        RefundRef.None.IsEmpty.ShouldBeTrue();
        AggregateId.None.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void OfferId_None_equals_default()
    {
        OfferId.None.ShouldBe(default(OfferId));
    }

    [Fact]
    public void AggregateId_None_value_is_empty_guid()
    {
        AggregateId.None.Value.ShouldBe(Guid.Empty);
    }
}
