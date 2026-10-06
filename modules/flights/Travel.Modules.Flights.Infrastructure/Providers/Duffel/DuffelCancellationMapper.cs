using System.Globalization;
using System.Net;
using System.Text.Json;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public static class DuffelCancellationMapper
{
    private static readonly Guid ValidationIdentity = new("00000000-0000-0000-0000-000000000001");

    public static CancellationQuoteResult MapQuote(
        DuffelCancellationDetailsDto dto,
        string order,
        CancellationEligibility? originalOrder,
        DateTimeOffset now
    )
    {
        if (
            !TryFacts(dto, order, originalOrder, now, out var quote, out var confirmed)
            || confirmed is not null
        )
            return new(CancellationQuoteOutcome.Unknown, null, CancellationReason.InvalidResponse);
        // Use the Core fresh-rules factory rather than duplicating currency/financial policy here.
        // This validation binding/hash is discarded; Application supplies the real owner/operation/scope.
        var validation = CancellationTerms.Create(
            new(
                1,
                ValidationIdentity,
                ValidationIdentity,
                ValidationIdentity,
                quote!.ProviderOrderRef,
                quote.ProviderCancellationRef,
                new string('0', 64),
                quote.Refund,
                quote.Destination,
                quote.Settlement,
                quote.ExpiresAt,
                "cancellation-v1"
            ),
            now
        );
        if (validation.IsError)
            return new(
                CancellationQuoteOutcome.UnsupportedTerms,
                quote,
                quote.ExpiresAt <= now
                    ? CancellationReason.TermsExpired
                    : CancellationReason.UnsupportedFinancialTerms
            );
        return new(CancellationQuoteOutcome.TermsAvailable, quote);
    }

    public static CancellationObservation MapObservation(
        DuffelCancellationDetailsDto dto,
        CancellationCorrelation correlation,
        DateTimeOffset now
    )
    {
        SupplierOrderCancellationFact? orderFact = null;
        if (
            dto is not null
            && dto.OrderId == correlation.ProviderOrderRef
            && Reference(dto.Id)
            && Date(dto.ConfirmedAt, false, out var cancelled)
            && cancelled <= now
        )
            orderFact = new(
                dto.OrderId!,
                dto.Id,
                cancelled!.Value,
                now,
                CancellationResolutionSource.SupplierApi
            );
        CancellationObservation Unknown(
            CancellationReason reason,
            CancellationQuoteFacts? quote = null
        ) =>
            new(
                correlation.ProviderOrderRef,
                correlation.ProviderCancellationRef,
                CancellationObservationState.Unknown,
                quote,
                null,
                now,
                CancellationResolutionSource.SupplierApi,
                reason,
                orderFact
            );
        if (dto is null || dto.OrderId != correlation.ProviderOrderRef)
            return Unknown(CancellationReason.IdentityMismatch);
        if (correlation.ProviderCancellationRef is null)
            return Unknown(CancellationReason.Uncorrelated);
        if (dto.Id != correlation.ProviderCancellationRef)
            return Unknown(CancellationReason.IdentityMismatch);
        var accepted = correlation.AcceptedTerms;
        var independent = accepted is null
            ? null
            : new CancellationEligibility(
                accepted.ProviderOrderRef,
                true,
                accepted.Settlement.UnpaidOrderVerified
                    ? CancellationProviderPaymentState.AwaitingPayment
                    : CancellationProviderPaymentState.Paid,
                null,
                accepted.Settlement
            );
        if (
            !TryFacts(
                dto,
                correlation.ProviderOrderRef,
                independent,
                now,
                out var facts,
                out var confirmed
            )
        )
            return Unknown(CancellationReason.InvalidResponse);
        facts = facts! with { ItineraryPartyHash = correlation.ItineraryPartyHash };
        if (confirmed is null)
            return new(
                correlation.ProviderOrderRef,
                dto.Id,
                CancellationObservationState.Pending,
                facts,
                null,
                now,
                CancellationResolutionSource.SupplierApi,
                CancellationReason.Pending
            );
        if (accepted is null)
            return Unknown(CancellationReason.Uncorrelated, facts);
        var evidence = CancellationEvidence.FromSupplierConfirmation(
            accepted,
            new(
                correlation.ProviderOrderRef,
                dto.Id!,
                correlation.ItineraryPartyHash,
                facts.Refund,
                facts.Destination,
                facts.Settlement,
                confirmed,
                now,
                CancellationResolutionSource.SupplierApi
            ),
            now
        );
        return evidence.IsError
            ? Unknown(CancellationReason.InconsistentEvidence, facts)
            : new(
                correlation.ProviderOrderRef,
                dto.Id,
                CancellationObservationState.Confirmed,
                facts,
                confirmed,
                now,
                CancellationResolutionSource.SupplierApi,
                CancellationReason.None,
                orderFact
            );
    }

    public static CancellationReason ClassifyFailure(
        HttpStatusCode status,
        IReadOnlyList<DuffelCancellationErrorDto>? errors,
        bool confirmation
    )
    {
        if (
            status != HttpStatusCode.UnprocessableEntity
            || errors is not { Count: 1 }
            || errors[0].Type is not ("invalid_state_error" or "airline_error")
        )
            return CancellationReason.ProviderUnavailable;
        return errors[0].Code switch
        {
            "order_cancellation_stale"
                when confirmation && errors[0].Type == "invalid_state_error" =>
                CancellationReason.StaleProposal,
            "order_not_cancellable" when !confirmation && errors[0].Type == "invalid_state_error" =>
                CancellationReason.NotCancellable,
            "already_cancelled" => CancellationReason.AlreadyCancelled,
            _ => CancellationReason.ProviderUnavailable,
        };
    }

    private static bool TryFacts(
        DuffelCancellationDetailsDto dto,
        string order,
        CancellationEligibility? originalOrder,
        DateTimeOffset now,
        out CancellationQuoteFacts? quote,
        out DateTimeOffset? confirmed
    )
    {
        quote = null;
        confirmed = null;
        if (
            dto is null
            || !Reference(dto.Id)
            || !Reference(dto.OrderId)
            || dto.OrderId != order
            || !Date(dto.CreatedAt, false, out var created)
            || created > now
            || !Date(dto.ExpiresAt, true, out var expiry)
            || !Date(dto.ConfirmedAt, true, out confirmed)
            || (expiry is not null && expiry < created)
            || (confirmed is not null && (confirmed < created || confirmed > now))
            || dto.AirlineCredits.ValueKind != JsonValueKind.Array
            || dto.AirlineCredits.EnumerateArray()
                .Any(item => item.ValueKind != JsonValueKind.Object)
            || dto.RefundAmount.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || dto.RefundCurrency.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
            || dto.RefundTo.ValueKind != JsonValueKind.String
        )
            return false;
        var destination = dto.RefundTo.GetString() switch
        {
            "balance" => CancellationRefundDestination.Balance,
            "card" => CancellationRefundDestination.Card,
            "arc_bsp_cash" => CancellationRefundDestination.ArcBspCash,
            "awaiting_payment" => CancellationRefundDestination.AwaitingPayment,
            "original_form_of_payment" => CancellationRefundDestination.OriginalFormOfPayment,
            _ => CancellationRefundDestination.Unknown,
        };
        Money? refund = null;
        if (dto.RefundAmount.ValueKind == JsonValueKind.String)
        {
            var amountText = dto.RefundAmount.GetString()!;
            if (
                amountText.Length is 0 or > 48
                || amountText != amountText.Trim()
                || !decimal.TryParse(
                    amountText,
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var amount
                )
                || amount < 0m
            )
                return false;
            if (dto.RefundCurrency.ValueKind == JsonValueKind.String)
            {
                var code = dto.RefundCurrency.GetString()!;
                if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
                    return false;
                var currency = CurrencyCode.Create(code);
                if (currency.IsError)
                    return false;
                var money = Money.Create(amount, currency.Value);
                if (money.IsError)
                    return false;
                refund = money.Value;
            }
        }
        var empty = dto.AirlineCredits.GetArrayLength() == 0;
        var originalProof =
            originalOrder is not null
            && originalOrder.ProviderOrderRef == order
            && originalOrder.PaymentState == CancellationProviderPaymentState.Paid
            && originalOrder.Settlement.Composition == SettlementComposition.CashOnly
            && originalOrder.Settlement.CreditsKnownEmpty
            && !originalOrder.Settlement.UnpaidOrderVerified
            && originalOrder.Settlement.OriginalCashOnlyVerified
            && originalOrder.Settlement.Provenance
                is CancellationResolutionSource.SupplierApi
                    or CancellationResolutionSource.OperatorVerified;
        var unpaidProof =
            originalOrder is not null
            && originalOrder.ProviderOrderRef == order
            && originalOrder.PaymentState == CancellationProviderPaymentState.AwaitingPayment
            && originalOrder.Settlement.Composition == SettlementComposition.Unpaid
            && originalOrder.Settlement.Provenance
                is CancellationResolutionSource.SupplierApi
                    or CancellationResolutionSource.OperatorVerified
            && originalOrder.Settlement.UnpaidOrderVerified
            && !originalOrder.Settlement.OriginalCashOnlyVerified;
        var composition = !empty
            ? SettlementComposition.Mixed
            : destination switch
            {
                CancellationRefundDestination.Balance
                or CancellationRefundDestination.Card
                or CancellationRefundDestination.ArcBspCash => SettlementComposition.CashOnly,
                CancellationRefundDestination.OriginalFormOfPayment when originalProof =>
                    SettlementComposition.CashOnly,
                CancellationRefundDestination.AwaitingPayment
                    when unpaidProof && refund?.Amount == 0m => SettlementComposition.Unpaid,
                _ => SettlementComposition.Unknown,
            };
        var financialSource =
            destination == CancellationRefundDestination.OriginalFormOfPayment && originalProof
                ? originalOrder!.Settlement.Provenance
                : CancellationResolutionSource.SupplierApi;
        var settlement = new CancellationSettlementFacts(
            composition,
            empty,
            financialSource,
            destination == CancellationRefundDestination.OriginalFormOfPayment && originalProof,
            destination == CancellationRefundDestination.AwaitingPayment && unpaidProof
        );
        quote = new(order, dto.Id!, refund, destination, settlement, expiry, created!.Value);
        return true;
    }

    private static bool Reference(string? value) =>
        value is { Length: > 0 and <= 256 }
        && value.All(c =>
            c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
        );

    private static bool Date(JsonElement value, bool nullable, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (nullable && value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var text = value.GetString()!;
        var explicitOffset =
            text.EndsWith('Z') || (text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-');
        if (
            text.Length > 64
            || !text.Contains('T')
            || !explicitOffset
            || !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var timestamp
            )
            || timestamp == default
        )
            return false;
        parsed = timestamp;
        return true;
    }
}
