using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
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
    ILogger<DuffelFlightBookingProvider> log
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
        var resp = await client.GetAsync($"/air/offers/{providerOfferRef}", ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            return FlightsErrors.OfferNotFound(providerOfferRef);

        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning(
                "Duffel RefreshOffer failed for {Ref}: {Status}",
                providerOfferRef,
                resp.StatusCode
            );
            return FlightsErrors.ProviderUnavailable("Duffel");
        }

        var dto =
            await ReadResponseAsync<DuffelOfferResponseDto>(resp, ct)
            ?? throw new InvalidOperationException("Empty Duffel offer response");

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

    public async Task<ErrorOr<HeldOrder>> HoldOfferAsync(
        BookableOffer offer,
        QuoteBinding binding,
        EquatableArray<BookingPassenger> passengers,
        CancellationToken ct
    )
    {
        var bindingValidation = binding.Validate();
        if (bindingValidation.IsError)
            return bindingValidation.Errors;
        if (offer.Party is null || offer.Party != binding.Party)
            return Error.Validation(
                "Flights.QuoteBindingInvalid",
                "Offer passenger binding is invalid."
            );
        if (binding.Party.SupportsHold != true)
            return Error.Validation(
                "Flights.HoldNotSupported",
                "Offer does not explicitly support hold."
            );
        if (binding.Party.RequiresIdentityDocuments != false)
            return Error.Validation(
                "Flights.IdentityDocumentsRequired",
                "Offer identity-document requirements are unsupported."
            );
        var validation = binding.ValidatePassengers(
            passengers,
            DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
        );
        if (validation.IsError)
            return validation.Errors;
        var byId = passengers.ToDictionary(p => p.Id);
        var body = new
        {
            type = "hold",
            selected_offers = new[] { offer.ProviderOfferRef },
            passengers = binding
                .Slots.Select(slot =>
                    MapPassenger(slot.SupplierReference.Value, byId[slot.Id].Details)
                )
                .ToArray(),
        };

        if (offer.ExpiresAt <= time.GetUtcNow())
            return FlightsErrors.OfferExpired;

        try
        {
            using var resp = await client.PostAsync("/air/orders", body, ct);
            if (!resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.Accepted)
                return FlightsErrors.HoldOutcomeUnknown;

            var dto = await ReadResponseAsync<DuffelOrderResponseDto>(resp, ct);
            if (dto?.Data is null || string.IsNullOrWhiteSpace(dto.Data.Id))
                return FlightsErrors.HoldOutcomeUnknown;

            // Preserve the original fallback when a successful held order omits its deadline.
            var holdExpiresAt = dto.Data.PaymentStatus?.PaymentRequiredBy ?? offer.ExpiresAt;
            return new HeldOrder(dto.Data.Id, holdExpiresAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // The request may have reached the supplier, even when no response could be read.
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

    public async Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        CancellationToken ct
    )
    {
        using var span = FlightsActivitySource.Source.StartActivity("duffel.confirm_order");
        span?.SetTag("provider.id", "duffel");
        span?.SetTag("provider.order_id", providerOrderId);

        // Recheck after wallet capture: never pay a supplier price the user did not accept.
        var validation = await ValidateConfirmationAsync(providerOrderId, expectedTotal, ct);
        if (validation.IsError)
            return validation.Errors;

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

            return new ConfirmedOrder(providerOrderId, time.GetUtcNow());
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
            log.LogWarning(
                "Duffel GetOrderStatus failed for {Id}: {Status}",
                providerOrderId,
                resp.StatusCode
            );
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
