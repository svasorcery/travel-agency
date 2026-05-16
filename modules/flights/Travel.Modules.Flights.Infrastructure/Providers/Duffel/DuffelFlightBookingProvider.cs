using System.Diagnostics;
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
            await resp.Content.ReadFromJsonAsync<DuffelOfferResponseDto>(JsonOpts, ct)
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
        PassengerInfo passenger,
        CancellationToken ct
    )
    {
        var body = new
        {
            type = "hold",
            selected_offers = new[] { offer.ProviderOfferRef },
            passengers = new[] { MapPassenger(passenger) },
        };

        var resp = await client.PostAsync("/air/orders", body, ct);

        if (!resp.IsSuccessStatusCode)
        {
            // A 422 means the offer is no longer available / hold not supported.
            // Map to OfferExpired so callers can prompt the user to re-quote.
            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                log.LogWarning("Duffel hold unavailable for offer {Ref}", offer.ProviderOfferRef);
                return FlightsErrors.OfferExpired;
            }

            log.LogWarning(
                "Duffel HoldOffer failed for {Ref}: {Status}",
                offer.ProviderOfferRef,
                resp.StatusCode
            );
            return FlightsErrors.ProviderUnavailable("Duffel");
        }

        var dto =
            await resp.Content.ReadFromJsonAsync<DuffelOrderResponseDto>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty Duffel order response");

        // Fall back to the offer's own ExpiresAt when Duffel omits payment_required_by.
        // Fabricating an arbitrary +20 min offset is incorrect and misleading to callers.
        var holdExpiresAt = dto.Data.PaymentStatus?.PaymentRequiredBy ?? offer.ExpiresAt;

        return new HeldOrder(dto.Data.Id, holdExpiresAt);
    }

    // -------------------------------------------------------------------------
    // ConfirmOrderAsync — GET order for amount, then POST /air/orders/{id}/payments
    // -------------------------------------------------------------------------

    public async Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        using var span = FlightsActivitySource.Source.StartActivity("duffel.confirm_order");
        span?.SetTag("provider.id", "duffel");
        span?.SetTag("provider.order_id", providerOrderId);

        // Fetch the order to retrieve total_amount / total_currency (not in the signature for M1).
        var getResp = await client.GetAsync($"/air/orders/{providerOrderId}", ct);
        if (!getResp.IsSuccessStatusCode)
        {
            log.LogWarning(
                "Duffel ConfirmOrder: could not fetch order {Id}: {Status}",
                providerOrderId,
                getResp.StatusCode
            );
            return FlightsErrors.PaymentFailed($"Could not retrieve order {providerOrderId}.");
        }

        var orderDto =
            await getResp.Content.ReadFromJsonAsync<DuffelOrderResponseDto>(JsonOpts, ct)
            ?? throw new InvalidOperationException("Empty Duffel order response on confirm");

        var payBody = new
        {
            type = "balance",
            amount = orderDto.Data.TotalAmount,
            currency = orderDto.Data.TotalCurrency,
        };

        // Duffel accepts Idempotency-Key on POST /air/orders/{id}/payments.
        // Sending the booking's stable AggregateId ensures the real gateway
        // deduplicates concurrent/retry confirm calls without double-charging.
        var payResp = await client.PostAsync(
            $"/air/orders/{providerOrderId}/payments",
            payBody,
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey },
            ct
        );

        if (!payResp.IsSuccessStatusCode)
        {
            // Log the raw provider body at Warning for diagnostics, but never surface
            // it in the domain error — it may contain PII or PCI-sensitive details.
            var rawBody = await payResp.Content.ReadAsStringAsync(ct);
            log.LogWarning(
                "Duffel payment failed for order {Id}: {Status} {RawBody}",
                providerOrderId,
                payResp.StatusCode,
                rawBody
            );
            return FlightsErrors.PaymentFailed($"Provider returned {(int)payResp.StatusCode}.");
        }

        return new ConfirmedOrder(providerOrderId, time.GetUtcNow());
    }

    // -------------------------------------------------------------------------
    // CancelOrderAsync — POST /air/order_cancellations (create)
    // Note: Duffel requires a two-step cancel (create + confirm). For M1 we only
    // issue the create-cancellation call; the confirm step is left for Task 52.
    // -------------------------------------------------------------------------

    public async Task<ErrorOr<Success>> CancelOrderAsync(
        string providerOrderId,
        CancellationToken ct
    )
    {
        var body = new { order_id = providerOrderId };
        var resp = await client.PostAsync("/air/order_cancellations", body, ct);

        if (!resp.IsSuccessStatusCode)
        {
            // Log the raw provider body at Warning for diagnostics, but never surface
            // it in the domain error — it may contain internal or sensitive details.
            var rawBody = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning(
                "Duffel CancelOrder failed for {Id}: {Status} {RawBody}",
                providerOrderId,
                resp.StatusCode,
                rawBody
            );
            return FlightsErrors.OrderNotCancellable($"Provider returned {(int)resp.StatusCode}.");
        }

        return Result.Success;
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
            await resp.Content.ReadFromJsonAsync<DuffelOrderResponseDto>(JsonOpts, ct)
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

    private static object MapPassenger(PassengerInfo p) =>
        new
        {
            given_name = p.GivenName,
            family_name = p.FamilyName,
            born_on = p.DateOfBirth.ToString("yyyy-MM-dd"),
            gender = p.Gender == Gender.Female ? "f" : "m",
            email = p.Email,
            phone_number = p.Phone.Value,
            type = "adult",
        };
}
