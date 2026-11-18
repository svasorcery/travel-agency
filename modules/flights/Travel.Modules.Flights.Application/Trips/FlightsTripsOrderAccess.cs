using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Marten;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Trips;

public sealed class FlightsTripsOrderAccess(
    IDocumentSession session,
    IOrderReadModelQueries projections,
    TimeProvider time
) : IFlightsTripsOrderAccess
{
    private const int PageSize = 20;
    private const int LastOffset = 10000;
    private static readonly JsonSerializerOptions ProjectionJson = new()
    {
        AllowDuplicateProperties = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new ExplicitOffsetTimestamp() },
    };

    public async Task<ErrorOr<FlightOrderObservation>> ReadAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty || orderId == Guid.Empty)
            return NotFound();
        try
        {
            var stream = await session.Events.FetchForWriting<BookingAggregate>(
                orderId,
                cancellationToken
            );
            return Observe(
                ownerId,
                orderId,
                stream.Aggregate,
                stream.CurrentVersion,
                time.GetUtcNow()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable();
        }
    }

    internal static ErrorOr<FlightOrderObservation> Observe(
        Guid ownerId,
        Guid orderId,
        BookingAggregate? booking,
        long? version,
        DateTimeOffset now
    )
    {
        if (
            booking is null
            || booking.Id != orderId
            || booking.OwnerUserId != ownerId
            || !booking.HasConsistentMutationOwner
            || booking.Status is BookingStatus.None or BookingStatus.OfferQuoted
        )
            return NotFound();
        if (
            version is null or <= 0
            || now == default
            || booking.PassengerCount is < 1 or > 9
            || Status(booking.Status.ToString()) is not { } status
        )
            return Unavailable();
        var closed =
            status is FlightOrderBookingStatus.Cancelled or FlightOrderBookingStatus.Refunded;
        var review =
            booking.HasUnresolvedCreation
            || booking.HasConfirmationBarrier
            || booking.HasSupplierCancellationReportBarrier
            || booking.CurrentCancellation is { IsTerminal: false }
            || !closed && booking.CreationBlocksConfirmation
            || status == FlightOrderBookingStatus.Held
                && (booking.ExpiresAt is null || booking.ExpiresAt <= now);
        return new FlightOrderObservation(
            orderId,
            ownerId,
            version.Value,
            now.ToUniversalTime(),
            status,
            review,
            booking.PassengerCount,
            Route(booking.Itinerary)
        );
    }

    public async Task<ErrorOr<FlightOrderCandidatePage>> ListCandidatesAsync(
        Guid ownerId,
        int offset,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ownerId == Guid.Empty)
            return NotFound();
        if (offset is < 0 or > LastOffset)
            return Error.Validation("Flights.InvalidCandidateOffset", "Invalid candidate offset.");
        try
        {
            var rows = await projections.ListCandidatesAsync(
                ownerId,
                PageSize + 1,
                offset,
                cancellationToken
            );
            if (rows.Count > PageSize + 1)
                return Unavailable();
            var items = new List<FlightOrderCandidate>(Math.Min(rows.Count, PageSize));
            var ids = new HashSet<Guid>();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    row.OwnerId != ownerId
                    || row.OrderId == Guid.Empty
                    || !ids.Add(row.OrderId)
                    || row.PassengerCount is < 1 or > 9
                    || Status(row.Status) is not { } status
                )
                    return Unavailable();
                if (items.Count < PageSize)
                    items.Add(
                        new(
                            row.OrderId,
                            status,
                            row.PassengerCount,
                            ProjectedRoute(row.ItineraryJson)
                        )
                    );
            }
            var hasMore = rows.Count > PageSize;
            var bounded = hasMore && offset + PageSize > LastOffset;
            return new FlightOrderCandidatePage(
                items,
                hasMore && !bounded ? offset + PageSize : null,
                bounded
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable();
        }
    }

    private static FlightOrderBookingStatus? Status(string value) =>
        value switch
        {
            nameof(BookingStatus.Held) => FlightOrderBookingStatus.Held,
            nameof(BookingStatus.Confirmed) => FlightOrderBookingStatus.Confirmed,
            nameof(BookingStatus.Ticketed) => FlightOrderBookingStatus.Ticketed,
            nameof(BookingStatus.Cancelled) => FlightOrderBookingStatus.Cancelled,
            nameof(BookingStatus.Refunded) => FlightOrderBookingStatus.Refunded,
            _ => null,
        };

    private static IReadOnlyList<FlightOrderLeg>? ProjectedRoute(string? json)
    {
        if (
            string.IsNullOrEmpty(json)
            || json.Length > ProjectedFlightOrderCandidate.MaximumItineraryBytes
            || Encoding.UTF8.GetByteCount(json)
                > ProjectedFlightOrderCandidate.MaximumItineraryBytes
        )
            return null;
        try
        {
            return Route(JsonSerializer.Deserialize<Itinerary>(json, ProjectionJson));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<FlightOrderLeg>? Route(Itinerary? itinerary)
    {
        if (itinerary is null)
            return null;
        var validated = Itinerary.Create(itinerary.Slices);
        if (
            validated.IsError
            || validated.Value.TotalDuration != itinerary.TotalDuration
            || itinerary.Slices.Any(slice =>
                slice.Segments.Any(s => s.DepartAt == default || s.ArriveAt == default)
            )
        )
            return null;
        return itinerary
            .Slices.Select(slice => new FlightOrderLeg(
                slice.Origin.Value,
                slice.Destination.Value,
                slice.DepartAt,
                slice.ArriveAt
            ))
            .ToArray();
    }

    // The default JSON reader assumes the machine's time zone for an offset-less timestamp.
    private sealed class ExplicitOffsetTimestamp : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type type,
            JsonSerializerOptions options
        )
        {
            if (
                reader.TokenType == JsonTokenType.String
                && reader.GetString() is { Length: >= 17 } value
                && value[10] == 'T'
                && (value.EndsWith('Z') || value[^3] == ':' && value[^6] is '+' or '-')
                && reader.TryGetDateTimeOffset(out var result)
            )
                return result;
            throw new JsonException("Route timestamp requires an explicit offset.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options
        ) => writer.WriteStringValue(value);
    }

    private static Error NotFound() =>
        Error.NotFound("Flights.TripsOrderNotFound", "Order was not found.");

    private static Error Unavailable() =>
        Error.Failure("Flights.TripsOrderUnavailable", "Order information is unavailable.");
}
