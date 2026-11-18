using ErrorOr;

namespace Travel.Modules.Trips.Core;

public sealed record TripFlightLink(Guid OrderId, DateTimeOffset LinkedAt)
{
    public override string ToString() => nameof(TripFlightLink);
}

public sealed class TripFlightLinks
{
    public const int MaximumLinks = 4;
    public Guid TripId { get; }
    public long Revision { get; }
    public IReadOnlyList<TripFlightLink> Links { get; }

    private TripFlightLinks(Guid tripId, long revision, TripFlightLink[] links)
    {
        TripId = tripId;
        Revision = revision;
        Links = Array.AsReadOnly(links);
    }

    public static ErrorOr<TripFlightLinks> Restore(
        Guid tripId,
        long revision,
        IReadOnlyList<TripFlightLink> links
    )
    {
        if (
            tripId == Guid.Empty
            || revision < 0
            || links is null
            || links.Count > MaximumLinks
            || links.Count > revision
        )
            return TripErrors.InvalidInput;
        var ids = new HashSet<Guid>();
        foreach (var link in links)
            if (
                link is null
                || link.OrderId == Guid.Empty
                || !ids.Add(link.OrderId)
                || !ValidTime(link.LinkedAt)
            )
                return TripErrors.InvalidInput;
        return new TripFlightLinks(tripId, revision, links.ToArray());
    }

    public ErrorOr<TripFlightLinks> Change(bool add, Guid orderId, DateTimeOffset now)
    {
        if (orderId == Guid.Empty)
            return TripErrors.InvalidInput;
        if (Links.Any(x => x.OrderId == orderId) == add)
            return this;
        if (add && Links.Count == MaximumLinks)
            return TripErrors.FlightLinkLimitReached;
        if (Revision == long.MaxValue)
            return TripErrors.RevisionExhausted;
        if (add && !ValidTime(now))
            return TripErrors.InvalidInput;
        return new TripFlightLinks(
            TripId,
            Revision + 1,
            add ? [.. Links, new(orderId, now)] : Links.Where(x => x.OrderId != orderId).ToArray()
        );
    }

    private static bool ValidTime(DateTimeOffset time) =>
        time != default && time.Offset == TimeSpan.Zero;
}
