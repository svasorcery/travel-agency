using Marten.Events.Aggregation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;

namespace Travel.Modules.Flights.Infrastructure.Marten;

/// <summary>
/// Marten replay dispatch stays outside Core. All state changes remain in the aggregate's Apply methods.
/// </summary>
public partial class BookingAggregateProjection : SingleStreamProjection<BookingAggregate, Guid>
{
    public BookingAggregate Create(OfferQuoted e)
    {
        var aggregate = new BookingAggregate();
        aggregate.Apply(e);
        return aggregate;
    }
}
