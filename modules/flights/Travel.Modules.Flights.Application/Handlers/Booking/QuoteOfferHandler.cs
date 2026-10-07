using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class QuoteOfferHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<QuotedOfferResult>> Handle(
        QuoteOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        IMartenOutbox outbox,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<QuoteOfferCommand> log,
        CancellationToken ct,
        IFlightAncillaryProvider? ancillaryProvider = null
    )
    {
        if (
            string.IsNullOrWhiteSpace(cmd.ProviderOfferRef)
            || cmd.PassengerCount is < 1 or > 9
            || cmd.Selections is { Length: > 256 }
            || cmd.UserId == Guid.Empty
        )
            return Error.Validation("Flights.CommandInvalid", "Quote request is invalid.");
        var provider = bookingProviders.FirstOrDefault(p => p.Id == cmd.Provider);
        if (provider is null)
            return FlightsErrors.ProviderUnavailable(cmd.Provider.Value);
        var id =
            cmd.AggregateId is { } existing && existing != Guid.Empty ? existing : Guid.NewGuid();
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(id, ct);
        var aggregate = stream.Aggregate;
        if (cmd.AggregateId is { } requested && requested != Guid.Empty)
        {
            if (aggregate is null || aggregate.OwnerUserId is { } owner && owner != cmd.UserId)
                return FlightsErrors.OfferNotFound(id.ToString());
            if (aggregate.Purchase is { HasServices: true } && !cmd.HasBookingAuthority)
                return Error.Forbidden(
                    "Flights.ServicePermissionRequired",
                    "Booking permission is required."
                );
            var decision = aggregate.DecideReQuote(cmd.ProviderOfferRef);
            if (decision is BookingTransitionDecision.Rejected rejected)
                return BookingTransitionErrorMapper.ToError(rejected.Reason);
            if (aggregate.PassengerCount != cmd.PassengerCount)
                return Error.Conflict(
                    "Flights.PassengerCountMismatch",
                    "Passenger count changed; start a new search."
                );
        }
        var selections =
            cmd.Selections
            ?? aggregate
                ?.Purchase?.Services.Select(s => new AncillarySelection(s.Reference, s.Quantity))
                .ToArray()
            ?? [];
        if (selections.Length > 0 && cmd.UserId is null)
            return Error.Unauthorized(
                "Flights.ServiceOwnerRequired",
                "Sign in before selecting services."
            );
        if (selections.Length > 0 && !cmd.HasBookingAuthority)
            return Error.Forbidden(
                "Flights.ServicePermissionRequired",
                "Booking permission is required."
            );
        AncillaryCatalogFacts? catalog = null;
        ErrorOr<BookableOffer> refreshed;
        if (selections.Length > 0)
        {
            if (ancillaryProvider is null)
                return FlightsErrors.ProviderUnavailable(cmd.Provider.Value);
            var result = await ancillaryProvider.ReadCatalogAsync(cmd.ProviderOfferRef, true, ct);
            if (result.IsError)
                return result.Errors;
            catalog = result.Value;
            refreshed = catalog.Offer;
        }
        else
            refreshed = await provider.RefreshOfferAsync(cmd.ProviderOfferRef, ct);
        if (refreshed.IsError)
            return refreshed.Errors;
        if (refreshed.Value.ExpiresAt <= time.GetUtcNow())
            return FlightsErrors.OfferExpired;
        var binding = QuoteBindingFactory.Create(
            refreshed.Value.Party,
            cmd.PassengerCount,
            aggregate?.QuoteBinding
        );
        if (binding.IsError)
            return binding.Errors;
        var purchase = catalog is null
            ? BookingPurchase.Create(
                binding.Value.Revision,
                aggregate?.OwnerUserId,
                refreshed.Value.TotalAmount,
                refreshed.Value.ExpiresAt,
                []
            )
            : BookingPurchaseFactory.Select(catalog, binding.Value, cmd.UserId, selections);
        if (purchase.IsError)
            return purchase.Errors;
        var now = time.GetUtcNow();
        var oldAmount = aggregate?.TotalAmount;
        var newAmount = purchase.Value.Total;
        if (aggregate is null)
            marten.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    refreshed.Value.Id,
                    refreshed.Value.Itinerary,
                    refreshed.Value.TotalAmount,
                    refreshed.Value.ExpiresAt,
                    refreshed.Value.ProviderOfferRef,
                    now,
                    refreshed.Value.FareConditions,
                    binding.Value
                ),
                new BookingPurchaseQuoted(purchase.Value, now)
            );
        else
        {
            stream.AppendOne(
                new OfferReQuoted(
                    refreshed.Value.Id,
                    oldAmount!,
                    refreshed.Value.TotalAmount,
                    now,
                    refreshed.Value,
                    binding.Value
                )
            );
            stream.AppendOne(new BookingPurchaseQuoted(purchase.Value, now));
        }
        var saved = await marten.SaveOrConcurrencyConflictAsync(outbox, id, [], ct);
        if (saved.IsError)
            return saved.Errors;
        metrics.RecordAggregateEventsAppended(
            aggregate is null ? nameof(OfferQuoted) : nameof(OfferReQuoted)
        );
        metrics.RecordAggregateEventsAppended(nameof(BookingPurchaseQuoted));
        var changed = oldAmount is not null && oldAmount != newAmount;
        return new QuotedOfferResult(
            id,
            refreshed.Value,
            binding.Value,
            changed,
            changed ? oldAmount : null,
            changed ? newAmount : null,
            purchase.Value
        );
    }
}
