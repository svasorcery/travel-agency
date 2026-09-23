namespace Travel.Modules.Flights.Application.ReadModels;

public sealed class BookingProjectionTransientException : Exception
{
    public BookingProjectionTransientException() { }

    public BookingProjectionTransientException(string message)
        : base(message) { }

    public BookingProjectionTransientException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class BookingProjectionTerminalException : Exception
{
    public BookingProjectionTerminalException() { }

    public BookingProjectionTerminalException(string message)
        : base(message) { }

    public BookingProjectionTerminalException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class BookingReadModelNotReadyException : Exception
{
    public BookingReadModelNotReadyException() { }

    public BookingReadModelNotReadyException(string message)
        : base(message) { }

    public BookingReadModelNotReadyException(string message, Exception innerException)
        : base(message, innerException) { }
}
