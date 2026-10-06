using System.Globalization;
using System.Net;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelFlightCancellationProvider(DuffelClient client, TimeProvider time)
    : IFlightCancellationProvider
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
    private const int MaximumResponseBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };

    public async Task<ErrorOr<CancellationEligibility>> InspectOrderAsync(
        string orderRef,
        CancellationToken ct
    )
    {
        if (!Reference(orderRef))
            return InvalidReference;
        using var budget = new CancellationTokenSource(Budget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            return await InspectCore(orderRef, linked.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }

    public async Task<ErrorOr<CancellationQuoteResult>> CreateTermsAsync(
        string orderRef,
        CancellationToken ct
    )
    {
        if (!Reference(orderRef))
            return InvalidReference;
        using var budget = new CancellationTokenSource(Budget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            var eligibility = await InspectCore(orderRef, linked.Token);
            if (eligibility.IsError)
                return new CancellationQuoteResult(
                    CancellationQuoteOutcome.Unknown,
                    null,
                    CancellationReason.ProviderUnavailable
                );
            if (!eligibility.Value.CancellationAvailable)
                return new CancellationQuoteResult(
                    CancellationQuoteOutcome.DefinitivelyRejected,
                    null,
                    CancellationReason.NotCancellable,
                    eligibility.Value.OrderCancellation
                );
            using var response = await client.PostAsync(
                "/air/order_cancellations",
                new { order_id = orderRef },
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token
            );
            if (!response.IsSuccessStatusCode)
            {
                var reason = await Failure(response, false, linked.Token);
                return new CancellationQuoteResult(
                    reason == CancellationReason.NotCancellable
                        ? CancellationQuoteOutcome.DefinitivelyRejected
                        : CancellationQuoteOutcome.Unknown,
                    null,
                    reason
                );
            }
            if (response.StatusCode == HttpStatusCode.Accepted)
                return new CancellationQuoteResult(
                    CancellationQuoteOutcome.Unknown,
                    null,
                    CancellationReason.Pending
                );
            var dto = await Read<DuffelCancellationDetailsResponseDto>(response, linked.Token);
            return dto?.Data is { } data
                ? DuffelCancellationMapper.MapQuote(
                    data,
                    orderRef,
                    eligibility.Value,
                    time.GetUtcNow()
                )
                : new CancellationQuoteResult(
                    CancellationQuoteOutcome.Unknown,
                    null,
                    CancellationReason.InvalidResponse
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CancellationQuoteResult(
                CancellationQuoteOutcome.Unknown,
                null,
                CancellationReason.ProviderUnavailable
            );
        }
    }

    public async Task<ErrorOr<CancellationEffectResult>> ConfirmAsync(
        CancellationTerms terms,
        CancellationToken ct
    )
    {
        if (
            terms is null
            || !Reference(terms.ProviderOrderRef)
            || !Reference(terms.ProviderCancellationRef)
        )
            return InvalidReference;
        if (terms.ExpiresAt <= time.GetUtcNow())
            return Error.Validation(
                "Flights.CancellationTermsExpired",
                "Cancellation terms expired."
            );
        using var budget = new CancellationTokenSource(Budget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            using (
                var freshResponse = await client.GetAsync(
                    $"/air/order_cancellations/{terms.ProviderCancellationRef}",
                    HttpCompletionOption.ResponseHeadersRead,
                    linked.Token
                )
            )
            {
                if (
                    !freshResponse.IsSuccessStatusCode
                    || freshResponse.StatusCode == HttpStatusCode.Accepted
                )
                    return new CancellationEffectResult(
                        CancellationEffectOutcome.Unknown,
                        null,
                        CancellationReason.ProviderUnavailable
                    );
                var current = (
                    await Read<DuffelCancellationDetailsResponseDto>(freshResponse, linked.Token)
                )?.Data;
                if (current is null)
                    return new CancellationEffectResult(
                        CancellationEffectOutcome.Unknown,
                        null,
                        CancellationReason.InvalidResponse
                    );
                var currentObservation = DuffelCancellationMapper.MapObservation(
                    current,
                    new(
                        terms.ProviderOrderRef,
                        terms.ProviderCancellationRef,
                        terms,
                        terms.ItineraryPartyHash
                    ),
                    time.GetUtcNow()
                );
                if (currentObservation.State == CancellationObservationState.Confirmed)
                    return new CancellationEffectResult(
                        CancellationEffectOutcome.Confirmed,
                        currentObservation
                    );
                if (
                    currentObservation.State != CancellationObservationState.Pending
                    || currentObservation.Quote is not { } quote
                    || !terms.MatchesQuote(quote)
                    || terms.ExpiresAt <= time.GetUtcNow()
                )
                    return new CancellationEffectResult(
                        CancellationEffectOutcome.Unknown,
                        currentObservation,
                        CancellationReason.InconsistentEvidence
                    );
            }
            using var response = await client.PostBodylessAsync(
                $"/air/order_cancellations/{terms.ProviderCancellationRef}/actions/confirm",
                linked.Token
            );
            if (!response.IsSuccessStatusCode)
            {
                var reason = await Failure(response, true, linked.Token);
                return new CancellationEffectResult(
                    reason == CancellationReason.StaleProposal
                        ? CancellationEffectOutcome.DefinitivelyRejected
                        : CancellationEffectOutcome.Unknown,
                    null,
                    reason
                );
            }
            if (response.StatusCode == HttpStatusCode.Accepted)
                return new CancellationEffectResult(
                    CancellationEffectOutcome.Unknown,
                    null,
                    CancellationReason.Pending
                );
            var dto = await Read<DuffelCancellationDetailsResponseDto>(response, linked.Token);
            if (dto?.Data is not { } data)
                return new CancellationEffectResult(
                    CancellationEffectOutcome.Unknown,
                    null,
                    CancellationReason.InvalidResponse
                );
            var observation = DuffelCancellationMapper.MapObservation(
                data,
                new(
                    terms.ProviderOrderRef,
                    terms.ProviderCancellationRef,
                    terms,
                    terms.ItineraryPartyHash
                ),
                time.GetUtcNow()
            );
            return new CancellationEffectResult(
                observation.State == CancellationObservationState.Confirmed
                    ? CancellationEffectOutcome.Confirmed
                    : CancellationEffectOutcome.Unknown,
                observation,
                observation.Reason
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CancellationEffectResult(
                CancellationEffectOutcome.Unknown,
                null,
                CancellationReason.ProviderUnavailable
            );
        }
    }

    public async Task<ErrorOr<CancellationObservation>> ObserveAsync(
        CancellationCorrelation correlation,
        CancellationToken ct
    )
    {
        if (
            correlation is null
            || !Reference(correlation.ProviderOrderRef)
            || (
                correlation.ProviderCancellationRef is not null
                && !Reference(correlation.ProviderCancellationRef)
            )
        )
            return InvalidReference;
        using var budget = new CancellationTokenSource(Budget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            CancellationObservation? first = null;
            if (correlation.ProviderCancellationRef is { } cancellation)
            {
                using var response = await client.GetAsync(
                    $"/air/order_cancellations/{cancellation}",
                    HttpCompletionOption.ResponseHeadersRead,
                    linked.Token
                );
                if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Accepted)
                {
                    var dto = await Read<DuffelCancellationDetailsResponseDto>(
                        response,
                        linked.Token
                    );
                    if (dto?.Data is { } data)
                        first = DuffelCancellationMapper.MapObservation(
                            data,
                            correlation,
                            time.GetUtcNow()
                        );
                    if (first?.State == CancellationObservationState.Confirmed)
                        return first;
                }
            }
            linked.Token.ThrowIfCancellationRequested();
            using var orderResponse = await client.GetAsync(
                $"/air/orders/{correlation.ProviderOrderRef}",
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token
            );
            if (
                orderResponse.IsSuccessStatusCode
                && orderResponse.StatusCode != HttpStatusCode.Accepted
            )
            {
                var order = (await Read<DuffelOrderResponseDto>(orderResponse, linked.Token))?.Data;
                if (order?.Id != correlation.ProviderOrderRef)
                    return Unknown(correlation, CancellationReason.IdentityMismatch);
                if (order.Cancellation is { } orderCancellation)
                {
                    var observed = DuffelCancellationMapper.MapObservation(
                        orderCancellation,
                        correlation,
                        time.GetUtcNow()
                    );
                    if (
                        observed.State == CancellationObservationState.Confirmed
                        || observed.Reason
                            is CancellationReason.IdentityMismatch
                                or CancellationReason.Uncorrelated
                                or CancellationReason.InconsistentEvidence
                    )
                        return observed;
                }
                if (order.CancelledAt is { } cancelledAt)
                {
                    var observedAt = time.GetUtcNow();
                    var fact =
                        cancelledAt != default && cancelledAt <= observedAt
                            ? new SupplierOrderCancellationFact(
                                correlation.ProviderOrderRef,
                                null,
                                cancelledAt,
                                observedAt,
                                CancellationResolutionSource.SupplierApi
                            )
                            : null;
                    return Unknown(correlation, CancellationReason.Uncorrelated, fact);
                }
            }
            return first
                ?? Unknown(
                    correlation,
                    correlation.ProviderCancellationRef is null
                        ? CancellationReason.Uncorrelated
                        : CancellationReason.Pending
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Unknown(correlation, CancellationReason.ProviderUnavailable);
        }
    }

    private async Task<ErrorOr<CancellationEligibility>> InspectCore(
        string orderRef,
        CancellationToken ct
    )
    {
        using var response = await client.GetAsync(
            $"/air/orders/{orderRef}",
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
            return Unavailable;
        var order = (await Read<DuffelOrderResponseDto>(response, ct))?.Data;
        if (order is null || order.Id != orderRef)
            return Unavailable;
        var state = order.PaymentStatus?.AwaitingPayment switch
        {
            true => CancellationProviderPaymentState.AwaitingPayment,
            false => CancellationProviderPaymentState.Paid,
            _ => CancellationProviderPaymentState.Unknown,
        };
        Money? total = null;
        if (
            decimal.TryParse(
                order.TotalAmount,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount
            )
            && amount >= 0m
            && order.TotalCurrency is { } code
        )
        {
            var currency = CurrencyCode.Create(code);
            if (!currency.IsError)
            {
                var value = Money.Create(amount, currency.Value);
                if (!value.IsError)
                    total = value.Value;
            }
        }
        return new CancellationEligibility(
            orderRef,
            order.CancelledAt is null
                && order.AvailableActions?.Contains("cancel", StringComparer.Ordinal) == true,
            state,
            total,
            new(
                state == CancellationProviderPaymentState.AwaitingPayment
                    ? SettlementComposition.Unpaid
                    : SettlementComposition.Unknown,
                false,
                CancellationResolutionSource.SupplierApi,
                false,
                state == CancellationProviderPaymentState.AwaitingPayment
            ),
            order.Cancellation?.Id,
            null,
            OrderFact(order)
        );
    }

    private SupplierOrderCancellationFact? OrderFact(DuffelOrderDto order)
    {
        var now = time.GetUtcNow();
        if (order.CancelledAt is { } at && at != default && at <= now)
            return new(order.Id!, null, at, now, CancellationResolutionSource.SupplierApi);
        return order.Cancellation is { } cancellation
            ? DuffelCancellationMapper
                .MapObservation(cancellation, new(order.Id!, cancellation.Id, null, ""), now)
                .OrderCancellation
            : null;
    }

    private async Task<CancellationReason> Failure(
        HttpResponseMessage response,
        bool confirmation,
        CancellationToken ct
    )
    {
        if (response.StatusCode != HttpStatusCode.UnprocessableEntity)
            return CancellationReason.ProviderUnavailable;
        var errors = await Read<DuffelCancellationErrorsDto>(response, ct);
        return DuffelCancellationMapper.ClassifyFailure(
            response.StatusCode,
            errors?.Errors,
            confirmation
        );
    }

    private static async Task<T?> Read<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0)
                break;
            if (bytes.Length + count > MaximumResponseBytes)
                return null;
            bytes.Write(buffer, 0, count);
        }
        if (bytes.Length == 0)
            return null;
        using var json = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 32 });
        if (!UniqueProperties(json.RootElement))
            return null;
        return json.RootElement.Deserialize<T>(JsonOptions);
    }

    private static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueProperties(property.Value))
                    return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (!UniqueProperties(item))
                    return false;
        return true;
    }

    private CancellationObservation Unknown(
        CancellationCorrelation correlation,
        CancellationReason reason,
        SupplierOrderCancellationFact? orderCancellation = null
    ) =>
        new(
            correlation.ProviderOrderRef,
            correlation.ProviderCancellationRef,
            CancellationObservationState.Unknown,
            null,
            null,
            time.GetUtcNow(),
            CancellationResolutionSource.SupplierApi,
            reason,
            orderCancellation
        );

    private static bool Reference(string? value) =>
        value is { Length: > 0 and <= 256 }
        && value.All(c =>
            c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
        );

    private static Error InvalidReference =>
        Error.Validation("Flights.CancellationReferenceInvalid", "Supplier reference invalid.");
    private static Error Unavailable =>
        Error.Failure("Flights.CancellationUnavailable", "Supplier facts unavailable.");
}
