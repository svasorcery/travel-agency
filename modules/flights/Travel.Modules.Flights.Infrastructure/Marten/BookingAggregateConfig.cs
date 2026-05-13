using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;

namespace Travel.Modules.Flights.Infrastructure.Marten;

public static class BookingAggregateConfig
{
    public static StoreOptions ConfigureFlightsBooking(this StoreOptions opts)
    {
        opts.Events.AddEventType(typeof(OfferQuoted));
        opts.Events.AddEventType(typeof(OfferReQuoted));
        opts.Events.AddEventType(typeof(OfferHeld));
        opts.Events.AddEventType(typeof(PaymentAuthorized));
        opts.Events.AddEventType(typeof(OrderConfirmed));
        opts.Events.AddEventType(typeof(OrderTicketed));
        opts.Events.AddEventType(typeof(OrderCancelled));
        opts.Events.AddEventType(typeof(OrderRefunded));

        opts.Projections.LiveStreamAggregation<BookingAggregate>();
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        return opts;
    }
}
