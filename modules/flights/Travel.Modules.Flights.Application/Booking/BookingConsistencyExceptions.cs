using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Booking;

public sealed class BookingWriteConflictException : Exception
{
    public BookingWriteConflictException() { }

    public BookingWriteConflictException(string message)
        : base(message) { }

    public BookingWriteConflictException(string message, Exception innerException)
        : base(message, innerException) { }

    public BookingWriteConflictException(Guid aggregateId, Exception innerException)
        : base($"Booking stream '{aggregateId}' changed concurrently.", innerException) =>
        AggregateId = aggregateId;

    public Guid AggregateId { get; }
}

public sealed class BookingCorrelationNotReadyException : Exception
{
    public BookingCorrelationNotReadyException() { }

    public BookingCorrelationNotReadyException(string message)
        : base(message) { }

    public BookingCorrelationNotReadyException(string message, Exception innerException)
        : base(message, innerException) { }

    private BookingCorrelationNotReadyException(string providerOrderId, bool isProviderOrder)
        : base($"Booking correlation is not ready for provider order '{providerOrderId}'.") =>
        ProviderOrderId = providerOrderId;

    public string? ProviderOrderId { get; }

    public static BookingCorrelationNotReadyException ForProviderOrder(string providerOrderId) =>
        new(providerOrderId, true);
}

public sealed class BookingTransitionPrerequisiteException : Exception
{
    public BookingTransitionPrerequisiteException() { }

    public BookingTransitionPrerequisiteException(string message)
        : base(message) { }

    public BookingTransitionPrerequisiteException(string message, Exception innerException)
        : base(message, innerException) { }

    public BookingTransitionPrerequisiteException(BookingRejection rejection)
        : base(
            $"Booking transition '{rejection.Transition}' is waiting for a prerequisite from state '{rejection.Status}'."
        ) => Rejection = rejection;

    public BookingRejection? Rejection { get; }
}

public sealed class BookingTransitionRejectedException : Exception
{
    public BookingTransitionRejectedException() { }

    public BookingTransitionRejectedException(string message)
        : base(message) { }

    public BookingTransitionRejectedException(string message, Exception innerException)
        : base(message, innerException) { }

    public BookingTransitionRejectedException(BookingRejection rejection)
        : base(
            $"Booking transition '{rejection.Transition}' was rejected in state '{rejection.Status}' ({rejection.Code})."
        ) => Rejection = rejection;

    public BookingRejection? Rejection { get; }
}

public sealed class BookingSourceOwnershipMissingException : Exception
{
    public BookingSourceOwnershipMissingException() { }

    public BookingSourceOwnershipMissingException(string message)
        : base(message) { }

    public BookingSourceOwnershipMissingException(string message, Exception innerException)
        : base(message, innerException) { }

    public BookingSourceOwnershipMissingException(Guid aggregateId)
        : base($"Booking stream '{aggregateId}' has no recorded owner.") =>
        AggregateId = aggregateId;

    public Guid AggregateId { get; }
}
