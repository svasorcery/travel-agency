namespace Travel.Modules.Flights.Core.Exceptions;

public sealed class InvalidBookingStateException : Exception
{
    public InvalidBookingStateException() { }

    public InvalidBookingStateException(string message)
        : base(message) { }

    public InvalidBookingStateException(string message, Exception innerException)
        : base(message, innerException) { }
}
