namespace Travel.Modules.Trips.Application.Itineraries;

/// <summary>Register one process-wide singleton. Two computations, no queue or retries.</summary>
public sealed class ItineraryAdmissionGate
{
    private readonly SemaphoreSlim slots = new(2, 2);

    internal bool TryEnter() => slots.Wait(0);

    internal void Exit() => slots.Release();
}
