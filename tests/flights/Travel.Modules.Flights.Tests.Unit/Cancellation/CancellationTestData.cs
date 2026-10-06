using System.Text.Json;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

internal static class CancellationTestData
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    internal static Money Money(decimal amount = 17.25m, string currency = "USD") =>
        Travel
            .Modules.Flights.Core.ValueObjects.Money.Create(
                amount,
                CurrencyCode.Create(currency).Value
            )
            .Value;

    internal static CancellationTermsInput Input() =>
        new(
            1,
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            Guid.Parse("00000000-0000-0000-0000-000000000102"),
            Guid.Parse("00000000-0000-0000-0000-000000000103"),
            "ord_fictional_1",
            "occ_fictional_1",
            new string('a', 64),
            Money(),
            CancellationRefundDestination.Balance,
            new(
                SettlementComposition.CashOnly,
                true,
                CancellationResolutionSource.SupplierApi,
                false,
                false
            ),
            Now.AddMinutes(10),
            "cancellation-v1"
        );

    internal static Money HistoricalMoney(decimal amount, string currency) =>
        JsonSerializer.Deserialize<Money>(
            JsonSerializer.Serialize(new { Amount = amount, Currency = new { Value = currency } })
        )!;

    internal static CancellationConfirmationFacts Confirmation(CancellationTerms terms) =>
        new(
            terms.ProviderOrderRef,
            terms.ProviderCancellationRef,
            terms.ItineraryPartyHash,
            terms.Refund,
            terms.Destination,
            terms.Settlement,
            Now.AddSeconds(1),
            Now.AddSeconds(2),
            CancellationResolutionSource.SupplierApi
        );
}
