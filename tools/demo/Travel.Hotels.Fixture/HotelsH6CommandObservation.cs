namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Observation(object? Value, string? Error);

internal static class HotelsH6CommandObservation
{
    internal static HotelH6Observation Read(bool cleanup, Func<object?> capture)
    {
        try
        {
            return new(capture(), null);
        }
        catch (Exception ex) when (cleanup)
        {
            return new(null, ex.GetType().Name);
        }
    }

    internal static Task<T> ExecuteAsync<T>(
        bool cleanup,
        Func<object?> capture,
        Func<HotelH6Observation, Task<T>> command
    ) => command(Read(cleanup, capture));
}
