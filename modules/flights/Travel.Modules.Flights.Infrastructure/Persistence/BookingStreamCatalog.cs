using System.Runtime.CompilerServices;
using Marten;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class BookingStreamCatalog(IDocumentStore store) : IBookingStreamCatalog
{
    public async IAsyncEnumerable<Guid> ReadIdsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var session = store.QuerySession();
        long after = 0;
        while (true)
        {
            // One first event per stream; keyset pagination avoids offsets and an unbounded ID set.
            // Select only metadata so an unreadable payload can still reach per-stream validation.
            var page = await session
                .Events.QueryAllRawEvents()
                .Where(x => x.Version == 1 && x.Sequence > after)
                .OrderBy(x => x.Sequence)
                .Take(128)
                .Select(x => new { x.StreamId, x.Sequence })
                .ToListAsync(ct);
            if (page.Count == 0)
                yield break;
            foreach (var entry in page)
            {
                ct.ThrowIfCancellationRequested();
                var state = await session.Events.FetchStreamStateAsync(entry.StreamId, ct);
                if (state?.AggregateType == typeof(BookingAggregate))
                    yield return entry.StreamId;
            }
            after = page[^1].Sequence;
        }
    }
}
