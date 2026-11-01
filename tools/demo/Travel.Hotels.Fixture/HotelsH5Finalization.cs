namespace Travel.Hotels.Fixture;

// The same completion boundary is exercised by the CLI and standalone regressions.
internal static class HotelsH5Finalization
{
    internal static Task<bool> FinishAsync(
        Func<Task> receipt,
        Func<Task> verify,
        Func<Task> integrity,
        Func<bool, IReadOnlyList<string>, Task> marker,
        Func<bool> eligible
    ) =>
        HotelsM2Completion.FinishAsync(
            async attempt =>
            {
                await attempt("ReceiptEvidence", receipt);
                await attempt("Identity", verify);
                await attempt("RawIntegrity", integrity);
            },
            () => Task.CompletedTask,
            marker,
            eligible
        );
}

internal sealed class HotelH5Progress
{
    internal bool StartupAttempted { get; set; }
    internal bool HostAcquired { get; set; }
    internal int PreflightSamples { get; set; }
    internal int PreflightSampleAttempts { get; set; }

    internal void RequireKnownCleanup()
    {
        if (StartupAttempted && !HostAcquired)
            throw new InvalidDataException(
                "Startup attempted; ownership/cleanup confirmation unavailable."
            );
    }
}
