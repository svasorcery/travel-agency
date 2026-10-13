namespace Travel.Modules.Support.Infrastructure.Persistence.Initialization;

public sealed class SupportSchemaReadiness
{
    private readonly TaskCompletionSource ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int initialized;
    public bool IsReady => Volatile.Read(ref initialized) == 1;

    public Task WaitAsync(CancellationToken ct) => ready.Task.WaitAsync(ct);

    public void MarkReady()
    {
        Volatile.Write(ref initialized, 1);
        ready.TrySetResult();
    }
}
