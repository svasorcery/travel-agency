using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelFlightBookingProvider(
    DuffelClient client,
    TimeProvider time,
    ILogger<DuffelFlightBookingProvider> log,
    DuffelOrderCreationClient? creationClient = null
) : IFlightBookingProvider
{
    // DTOs use [JsonPropertyName] attributes; Web defaults handle the rest.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ProviderId Id => ProviderId.Duffel;

    // -------------------------------------------------------------------------
    // RefreshOfferAsync — GET /air/offers/{ref}
    // -------------------------------------------------------------------------

    public async Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
        string providerOfferRef,
        CancellationToken ct
    )
    {
        if (!DuffelAncillaryMapper.Reference(providerOfferRef))
            return Error.Validation("Flights.OfferReferenceInvalid", "Offer reference is invalid.");
        var resp = await client.GetAsync($"/air/offers/{providerOfferRef}", ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return FlightsErrors.OfferNotFound(providerOfferRef);

        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Duffel RefreshOffer failed: {Status}", resp.StatusCode);
            return FlightsErrors.ProviderUnavailable("Duffel");
        }

        var dto =
            await ReadResponseAsync<DuffelOfferResponseDto>(resp, ct)
            ?? throw new InvalidOperationException("Empty Duffel offer response");
        if (DuffelAncillaryMapper.HasUnsupportedPricing(dto.Data))
            return Error.Validation(
                "Flights.PricingIntentUnsupported",
                "Offer pricing is unsupported."
            );

        var mapped = DuffelOfferMapper.Map(dto.Data, time);
        if (mapped.IsError)
            return mapped.FirstError;

        if (mapped.Value.ExpiresAt <= time.GetUtcNow())
            return FlightsErrors.OfferExpired;

        return mapped.Value;
    }

    // -------------------------------------------------------------------------
    // HoldOfferAsync — POST /air/orders (type = "hold")
    // -------------------------------------------------------------------------

    public async Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
        BookableOffer offer,
        QuoteBinding binding,
        EquatableArray<BookingPassenger> passengers,
        BookingPurchase purchase,
        Guid attemptId,
        CancellationToken ct
    )
    {
        BookingCreationObservation NoEffects(string reason) =>
            new(
                BookingCreationOutcome.NotCreated,
                null,
                null,
                false,
                true,
                reason,
                time.GetUtcNow(),
                BookingEvidenceSource.TravelAdmission,
                true
            );
        if (
            creationClient is null
            || attemptId == Guid.Empty
            || binding.Validate().IsError
            || offer.Party != binding.Party
            || purchase.QuoteRevision != binding.Revision
            || binding.Party.SupportsHold != true
            || binding.Party.RequiresIdentityDocuments != false
            || binding
                .ValidatePassengers(passengers, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime))
                .IsError
        )
            return NoEffects("RequestInvalid");
        if (offer.ExpiresAt <= time.GetUtcNow())
            return NoEffects("OfferExpired");
        var byId = passengers.ToDictionary(p => p.Id.Value);
        var body = new
        {
            type = "hold",
            selected_offers = new[] { offer.ProviderOfferRef },
            passengers = binding
                .Slots.Select(slot =>
                    MapPassenger(slot.SupplierReference.Value, byId[slot.Id.Value].Details)
                )
                .ToArray(),
            services = purchase
                .Services.Select(s => new { id = s.Reference, quantity = s.Quantity })
                .ToArray(),
            metadata = new Dictionary<string, string>
            {
                ["travel_creation"] = attemptId.ToString("N"),
            },
        };
        string? known = null;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(130), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            using var response = await creationClient.CreateAsync(body, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var failure = await DuffelBoundedJson.Read<JsonElement>(
                    response,
                    512 * 1024,
                    linked.Token
                );
                if (
                    failure.ValueKind == JsonValueKind.Object
                    && failure.TryGetProperty("errors", out var errors)
                    && errors.ValueKind == JsonValueKind.Array
                    && errors.GetArrayLength() is > 0 and <= 32
                    && errors
                        .EnumerateArray()
                        .All(e =>
                            e.ValueKind == JsonValueKind.Object
                            && e.TryGetProperty("code", out var code)
                            && code.ValueKind == JsonValueKind.String
                            && (
                                response.StatusCode == HttpStatusCode.ServiceUnavailable
                                || response.StatusCode
                                    is HttpStatusCode.BadRequest
                                        or HttpStatusCode.UnprocessableEntity
                                    && code.GetString()
                                        is "ancillary_service_not_available"
                                            or "services_not_allowed_for_order_type"
                            )
                        )
                )
                    return new BookingCreationObservation(
                        BookingCreationOutcome.NotCreated,
                        null,
                        null,
                        false,
                        true,
                        "SupplierRejected",
                        time.GetUtcNow(),
                        PositiveNoEffects: true
                    );
                return Unknown(null, true);
            }
            if (response.StatusCode == HttpStatusCode.Accepted)
                return Unknown(null, true);
            var order = (
                await DuffelBoundedJson.Read<DuffelOrderResponseDto>(
                    response,
                    2 * 1024 * 1024,
                    linked.Token
                )
            )?.Data;
            if (order is not null && DuffelAncillaryMapper.Reference(order.Id))
                known = order.Id;
            if (order is null)
                return Unknown(known, true);
            var mapped = DuffelBookedServicesMapper.Map(
                order,
                offer,
                binding,
                purchase,
                attemptId,
                time
            );
            if (
                mapped.IsError
                || !mapped.Value.AwaitingPayment
                || mapped.Value.Cancelled
                || mapped.Value.PaymentRequiredBy <= time.GetUtcNow()
            )
                return Unknown(known, true);
            return new BookingCreationObservation(
                mapped.Value.Matches(purchase)
                    ? BookingCreationOutcome.Matches
                    : BookingCreationOutcome.CreatedWithDifferences,
                mapped.Value,
                order.Id,
                true,
                true,
                "OrderObserved",
                time.GetUtcNow()
            );
        }
        catch (Exception)
        {
            return Unknown(known, true);
        }
    }

    private BookingCreationObservation Unknown(string? known, bool senderCompleted) =>
        new(
            BookingCreationOutcome.ManualReviewRequired,
            null,
            known,
            known is not null,
            senderCompleted,
            "OrderUnproven",
            time.GetUtcNow()
        );

    public async Task<ErrorOr<BookedOrderFacts>> ReadOrderForBookingAsync(
        string orderId,
        BookableOffer offer,
        QuoteBinding binding,
        BookingPurchase purchase,
        Guid attemptId,
        CancellationToken ct
    )
    {
        if (!DuffelAncillaryMapper.Reference(orderId))
            return Error.Validation("Flights.OrderFactsInvalid", "Order reference is unavailable.");
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            using var response = await client.GetAsync(
                $"/air/orders/{orderId}",
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token
            );
            if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
                return FlightsErrors.HoldOutcomeUnknown;
            var order = (
                await DuffelBoundedJson.Read<DuffelOrderResponseDto>(
                    response,
                    2 * 1024 * 1024,
                    linked.Token
                )
            )?.Data;
            return order is null || order.Id != orderId
                ? FlightsErrors.HoldOutcomeUnknown
                : DuffelBookedServicesMapper.Map(order, offer, binding, purchase, attemptId, time);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FlightsErrors.HoldOutcomeUnknown;
        }
    }

    public async Task<ErrorOr<Success>> ValidateConfirmationAsync(
        string providerOrderId,
        Money expectedTotal,
        CancellationToken ct
    )
    {
        try
        {
            using var response = await client.GetAsync($"/air/orders/{providerOrderId}", ct);
            if (!response.IsSuccessStatusCode)
                return FlightsErrors.ConfirmationOutcomeUnknown;

            var dto = await ReadResponseAsync<DuffelOrderResponseDto>(response, ct);
            var order = dto?.Data;
            if (
                order is null
                || !string.Equals(order.Id, providerOrderId, StringComparison.Ordinal)
                || order.CancelledAt is not null
                || order.PaymentStatus?.AwaitingPayment != true
                || order.PaymentStatus.PaymentRequiredBy is null
                || !TryAmount(order.TotalAmount, out var amount)
                || string.IsNullOrWhiteSpace(order.TotalCurrency)
            )
                return FlightsErrors.ConfirmationOutcomeUnknown;

            if (order.PaymentStatus.PaymentRequiredBy <= time.GetUtcNow())
                return FlightsErrors.HoldExpired;

            if (
                amount != expectedTotal.Amount
                || order.TotalCurrency != expectedTotal.Currency.Value
            )
                return FlightsErrors.OrderPriceChanged;

            return Result.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
    }

    public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        CancellationToken ct
    ) => ConfirmCoreAsync(providerOrderId, payment, expectedTotal, null, ct);

    private async Task<ErrorOr<ConfirmedOrder>> ConfirmCoreAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        Func<CancellationToken, Task<bool>>? canDispatch,
        CancellationToken ct,
        BookingOrderContext? context = null
    )
    {
        using var span = FlightsActivitySource.Source.StartActivity("duffel.confirm_order");
        span?.SetTag("provider.id", "duffel");

        // Recheck after wallet capture: never pay a supplier price the user did not accept.
        var validation = await ValidateConfirmationAsync(providerOrderId, expectedTotal, ct);
        if (validation.IsError)
            return validation.Errors;
        DateTimeOffset? paymentRequiredBy = null;
        if (context is not null)
        {
            var current = await ReadOrderForBookingAsync(
                providerOrderId,
                context.Offer,
                context.Binding,
                context.Purchase,
                context.AttemptId,
                ct
            );
            if (
                current.IsError
                || !current.Value.AwaitingPayment
                || current.Value.Cancelled
                || current.Value.PaymentRequiredBy <= time.GetUtcNow()
                || !current.Value.Matches(context.Purchase)
            )
                return FlightsErrors.ConfirmationOutcomeUnknown;
            paymentRequiredBy = current.Value.PaymentRequiredBy;
        }

        if (canDispatch is not null && !await canDispatch(ct))
            return FlightsErrors.ConfirmationOutcomeUnknown;
        if (paymentRequiredBy is { } deadline && deadline <= time.GetUtcNow())
            return FlightsErrors.HoldExpired;
        var payBody = new
        {
            order_id = providerOrderId,
            payment = new
            {
                type = "balance",
                amount = expectedTotal.Amount.ToString(
                    "0.00##########################",
                    CultureInfo.InvariantCulture
                ),
                currency = expectedTotal.Currency.Value,
            },
        };

        try
        {
            // No documented supplier deduplication guarantee: do not attach a guessed key or retry POST.
            using var response = await client.PostAsync("/air/payments", payBody, ct);
            if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Accepted)
                return FlightsErrors.ConfirmationOutcomeUnknown;

            var dto = await ReadResponseAsync<DuffelPaymentResponseDto>(response, ct);
            var receipt = dto?.Data;
            if (
                receipt is null
                || receipt.Status != "succeeded"
                || string.IsNullOrWhiteSpace(receipt.Id)
                || receipt.OrderId != providerOrderId
                || receipt.Type != "balance"
                || receipt.Currency != expectedTotal.Currency.Value
                || !TryAmount(receipt.Amount, out var amount)
                || amount != expectedTotal.Amount
            )
                return FlightsErrors.ConfirmationOutcomeUnknown;

            var paymentEvidence = SupplierPaymentEvidence.Create(
                receipt.Id,
                expectedTotal,
                SupplierPaymentKind.Balance
            );
            if (paymentEvidence.IsError)
                return FlightsErrors.ConfirmationOutcomeUnknown;
            BookingServiceProof? proof = null;
            if (context is not null)
            {
                var paid = await ReadOrderForBookingAsync(
                    providerOrderId,
                    context.Offer,
                    context.Binding,
                    context.Purchase,
                    context.AttemptId,
                    ct
                );
                if (
                    paid.IsError
                    || paid.Value.AwaitingPayment
                    || paid.Value.Cancelled
                    || !paid.Value.Matches(context.Purchase)
                )
                    return FlightsErrors.ConfirmationOutcomeUnknown;
                proof = new(context.Purchase.QuoteRevision, providerOrderId, paid.Value.Services);
            }
            return new ConfirmedOrder(
                providerOrderId,
                time.GetUtcNow(),
                paymentEvidence.Value,
                proof
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
    }

    public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        Func<CancellationToken, Task<bool>> canDispatch,
        CancellationToken ct
    ) => ConfirmCoreAsync(providerOrderId, payment, expectedTotal, canDispatch, ct);

    public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        BookingOrderContext context,
        Func<CancellationToken, Task<bool>> canDispatch,
        CancellationToken ct
    ) => ConfirmCoreAsync(providerOrderId, payment, expectedTotal, canDispatch, ct, context);

    private static bool TryAmount(string? value, out decimal amount) =>
        decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount
        )
        && amount >= 0;

    // -------------------------------------------------------------------------
    // CancelOrderAsync — unavailable until create/confirm and recovery are complete.
    // -------------------------------------------------------------------------

    public Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct)
    {
        // Creating a cancellation quote does not cancel the order. Until confirmed
        // cancellation and ambiguous-outcome recovery are implemented, fail before HTTP.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<ErrorOr<Success>>(FlightsErrors.ProviderCancellationNotSupported);
    }

    // -------------------------------------------------------------------------
    // GetOrderStatusAsync — GET /air/orders/{id}
    // -------------------------------------------------------------------------

    public async Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
        string providerOrderId,
        CancellationToken ct
    )
    {
        var resp = await client.GetAsync($"/air/orders/{providerOrderId}", ct);

        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Duffel GetOrderStatus failed: {Status}", resp.StatusCode);
            return FlightsErrors.ProviderUnavailable("Duffel");
        }

        var dto =
            await ReadResponseAsync<DuffelOrderResponseDto>(resp, ct)
            ?? throw new InvalidOperationException("Empty Duffel order response");

        var order = dto.Data;
        var ticketNumbersArr = (order.Documents ?? [])
            .Where(d => d.Type == "ticket")
            .Select(d => d.UniqueIdentifier)
            .ToArray();

        var ticketNumbers = new EquatableArray<string>(ticketNumbersArr);

        // Ticketed takes priority over Confirmed (documents already issued).
        // Cancelled is detected by the presence of cancelled_at.
        var statusKind =
            order.CancelledAt.HasValue ? OrderStatusKind.Cancelled
            : ticketNumbers.Count > 0 ? OrderStatusKind.Ticketed
            : OrderStatusKind.Confirmed;

        return new OrderStatus(providerOrderId, statusKind, ticketNumbers);
    }

    // -------------------------------------------------------------------------
    // Passenger mapping helper
    // -------------------------------------------------------------------------

    private static object MapPassenger(string supplierId, BookingPassengerDetails details)
    {
        var p = details.Passenger;
        return new
        {
            id = supplierId,
            title = details.Title.Code,
            given_name = p.GivenName,
            family_name = p.FamilyName,
            born_on = p.DateOfBirth.ToString("yyyy-MM-dd"),
            gender = p.Gender == Gender.Female ? "f" : "m",
            email = p.Email,
            phone_number = p.Phone.Value,
        };
    }

    private static async Task<T?> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier response could not be read.");
        }
    }
}
