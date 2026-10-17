namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public sealed class HotelsSchemaReadiness
{
    private readonly TaskCompletionSource ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public Task WaitAsync(CancellationToken ct) => ready.Task.WaitAsync(ct);

    public void MarkReady() => ready.TrySetResult();
}
