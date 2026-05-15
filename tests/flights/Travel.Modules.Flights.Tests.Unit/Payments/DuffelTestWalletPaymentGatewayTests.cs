using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Payments;
using Travel.Shared.Abstractions;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Payments;

public sealed class DuffelTestWalletPaymentGatewayTests
{
    private static readonly Money Amount = Money
        .Create(100m, CurrencyCode.Create("USD").Value)
        .Value;
    private static readonly DuffelTestWalletPaymentGateway Gateway = new();

    [Fact]
    public async Task AuthorizeAsync_returns_non_error_PaymentRef()
    {
        var result = await Gateway.AuthorizeAsync(Amount, "idem-key-1", CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task CaptureAsync_returns_Success()
    {
        var paymentRef = new PaymentRef(Guid.NewGuid());

        var result = await Gateway.CaptureAsync(paymentRef, CancellationToken.None);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task RefundAsync_returns_non_error_RefundRef()
    {
        var paymentRef = new PaymentRef(Guid.NewGuid());

        var result = await Gateway.RefundAsync(paymentRef, Amount, CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Class_carries_TestOnly_attribute()
    {
        typeof(DuffelTestWalletPaymentGateway)
            .GetCustomAttribute<TestOnlyAttribute>()
            .ShouldNotBeNull();
    }

    // =========================================================================
    // Task 4.2 — idempotency key
    // =========================================================================

    [Fact]
    public async Task TestWallet_honours_idempotency_key()
    {
        // Two AuthorizeAsync calls with the SAME idempotency key must return the same PaymentRef.
        const string key = "stable-booking-id-N";

        var first = await Gateway.AuthorizeAsync(Amount, key, CancellationToken.None);
        var second = await Gateway.AuthorizeAsync(Amount, key, CancellationToken.None);

        first.IsError.ShouldBeFalse();
        second.IsError.ShouldBeFalse();
        first.Value.Value.ShouldBe(second.Value.Value);
    }

    [Fact]
    public async Task TestWallet_different_keys_return_different_refs()
    {
        var first = await Gateway.AuthorizeAsync(Amount, "key-A", CancellationToken.None);
        var second = await Gateway.AuthorizeAsync(Amount, "key-B", CancellationToken.None);

        first.IsError.ShouldBeFalse();
        second.IsError.ShouldBeFalse();
        first.Value.Value.ShouldNotBe(second.Value.Value);
    }
}
