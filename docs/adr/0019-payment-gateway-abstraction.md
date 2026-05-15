# 0019. Payment Gateway Abstraction and TestOnly Enforcement

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

## Context

The M1 booking saga requires a payment step between `OfferHeld` and `OrderConfirmed`. The platform is a sandbox showcase: no real payment processor is integrated in M1, and no real money changes hands. Duffel's test environment provides a "test wallet" mechanism — a sandbox-only payment instrument that always succeeds and requires no card data.

Two design pressures pull in opposite directions. On one side, the saga should be implemented against a real abstraction so that a genuine payment processor (Stripe, CloudPayments, or similar) can be introduced in a future milestone without rewriting saga logic. On the other side, the sandbox-only implementation must be prevented from being registered in a production environment — a silent test-wallet-in-production leak would be a security and financial control failure.

M1 also introduces a `[TestOnly]` attribute as a general mechanism for marking types that must never appear in production DI registrations. The payment gateway is the first and primary use case for this attribute, but the attribute is defined in `Travel.Shared.Abstractions` for reuse by any module.

## Decision

We define `IPaymentGateway` in `Travel.Modules.Flights.Core/Providers/` with three methods mirroring the industry-standard authorize-capture-refund pattern:

```csharp
public interface IPaymentGateway
{
    Task<ErrorOr<PaymentRef>> AuthorizeAsync(Money amount, string idempotencyKey, CancellationToken ct);
    Task<ErrorOr<Unit>> CaptureAsync(PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<RefundRef>> RefundAsync(PaymentRef payment, Money amount, CancellationToken ct);
}
```

The sole M1 implementation is `DuffelTestWalletPaymentGateway`, located in `Infrastructure/Payments/`. It is decorated with the new `[TestOnly]` attribute:

```csharp
[TestOnly]
public sealed class DuffelTestWalletPaymentGateway : IPaymentGateway { ... }
```

`[TestOnly]` is a custom attribute defined in `Travel.Shared.Abstractions`. An ArchUnitNET architecture test (part of the M1 test suite, enforced on every CI build) asserts that no class bearing `[TestOnly]` is registered in a DI container whose environment is not `Development`. The environment is detected via the `ASPNETCORE_ENVIRONMENT` environment variable read at test time. This makes the sandbox-only constraint machine-enforceable, not just a convention.

When a real payment processor is introduced, the implementor creates a new class (e.g., `StripePaymentGateway : IPaymentGateway`) without the `[TestOnly]` attribute, implements the three-method contract, and updates the DI registration in `FlightsModuleStartup.cs`. The booking saga — `QuoteOfferHandler`, `HoldOfferHandler`, `ConfirmOrderHandler`, `CancelOrderHandler` — requires no changes.

The DI registration in M1 is environment-guarded:

```csharp
// M1 ships only the [TestOnly] Duffel test wallet — never register it in Production.
if (!environment.IsProduction())
    services.AddSingleton<IPaymentGateway, DuffelTestWalletPaymentGateway>();
```

This `if (!IsProduction())` guard is the primary runtime enforcement. An additional `TestOnlyGuard.Verify(services, environment)` call at startup throws `InvalidOperationException` if any `[TestOnly]`-decorated type is present in a Production `IServiceCollection`, providing defence-in-depth. The marker-presence architecture test (`[TestOnly]` attribute on `DuffelTestWalletPaymentGateway`) is verified by the ArchUnitNET suite on every CI build.

## Alternatives Considered

### Option A: Direct Duffel test wallet calls in the booking saga — no abstraction

The saga calls Duffel's payment API directly, with no `IPaymentGateway` interface. Payment logic is embedded in `ConfirmOrderHandler`.

Rejected because it couples the saga to a specific payment mechanism and to Duffel specifically. Introducing any real PSP would require modifying `ConfirmOrderHandler` — a core saga file — rather than adding a new implementation class. The saga's single responsibility is orchestrating booking state transitions; payment mechanism selection is infrastructure.

### Option B: Full PSP-grade abstraction with payment-method tokens, 3DS, idempotency-key forwarding, and webhook handling

A richer interface covering card tokenization, 3D Secure challenge flows, PSP-side idempotency keys, and payment status webhooks.

Rejected as YAGNI for M1. The showcase scope requires demonstrating that payments can be modelled as a seam, not that every PSP feature is implemented. The three-method contract (`Authorize / Capture / Refund`) covers the actual M1 saga operations. A richer interface would require implementing stubs for methods never called in M1, inflating both the interface and the test surface. When a real PSP integration is designed (a future milestone or ADR), the interface can be evolved at that point with concrete requirements.

## Consequences

### Positive
- The booking saga is decoupled from any specific payment mechanism. Adding a real PSP requires implementing `IPaymentGateway` once and updating one DI registration line; no saga code changes.
- The environment-guarded `if (!IsProduction())` registration combined with `TestOnlyGuard.Verify` at startup makes the sandbox-only constraint machine-enforceable. It is impossible to ship `DuffelTestWalletPaymentGateway` to a Production environment without a startup exception or a CI failure from the ArchUnitNET marker-presence test.
- The `[TestOnly]` attribute in `Travel.Shared.Abstractions` is reusable. Any module that needs a sandbox-only implementation can apply the same attribute and benefit from the same architecture test assertion.
- The three-method `Authorize / Capture / Refund` contract is recognisable to developers familiar with Stripe, Braintree, or Adyen; onboarding a future PSP integration author requires no explanation of a custom protocol.

### Negative / Trade-offs
- The `TestOnlyGuard` checks `ASPNETCORE_ENVIRONMENT == Production` at startup. If a deployment environment is misconfigured (e.g., staging runs as `Development`), the runtime guard allows the `[TestOnly]` registration. Operational discipline around environment variables is a prerequisite for this enforcement to be effective. The guard is defense-in-depth alongside the `if (!IsProduction())` conditional registration.
- `IPaymentGateway` is defined in `Core`, which is the correct layer for a port, but it references `Money` and `PaymentRef` value objects that must also be defined in `Core`. Any PSP-specific concerns (e.g., currency rounding rules, refund eligibility) that differ between gateways must be resolved by the implementing class in `Infrastructure`, not pushed into the interface.

### Neutral
- The M1 `DuffelTestWalletPaymentGateway` uses Duffel's sandbox balance API (`POST /payments/payments` with `type: "balance"`). In Duffel's sandbox, this call always succeeds regardless of amount. The gateway's `AuthorizeAsync` and `CaptureAsync` return `Ok` unconditionally in the test environment. Failure paths are tested via unit tests with a mock, not via sandbox API calls.

## Out of Scope

- Selection logic when multiple `IPaymentGateway` implementations are registered — M1 registers exactly one; multi-gateway routing is a future concern.
- Partial refund support — `RefundAsync` in M1 accepts a `Money amount` parameter to support the full contract, but M1 only exercises full refunds triggered by airline-initiated cancellations. Partial refund business logic is M3 scope.
- 3D Secure, card tokenization, and payment-method storage — not required for Duffel test wallet and not modelled in the M1 interface. A future PSP milestone will extend or replace this interface.
- Chargeback and dispute handling — explicitly out of scope for the platform at this stage.

## References

- Flights M1 spec: `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §3 (decision 1), §5.1, §5.2, §8
- ADR 0013: `docs/adr/0013-flights-provider-abstraction.md`
- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md`
- ADR 0008: `docs/adr/0008-result-pattern-error-or.md`
- Duffel Payments API (sandbox): https://duffel.com/docs/api/v2/payments
