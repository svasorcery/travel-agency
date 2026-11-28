using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Travel.Modules.Trips.Application;

namespace Travel.Modules.Trips.Api.Endpoints;

internal sealed class SavedOptionPreviewReadiness(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<SavedOptionPreviewReadiness> logger
)
{
    // Generation has its own two-second deadline. A storage probe is optional for the finished card.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim slots = new(2, 2);

    public async Task<bool> IsReadyAsync(CancellationToken request)
    {
        if (!slots.Wait(0, request))
            return false;

        var started = clock.GetTimestamp();
        var scope = scopes.CreateAsyncScope();
        var cancellation = new CancellationTokenSource();
        // Provider startup and cancellation callbacks can both stall. Neither runs on the HTTP path.
        var check = Task.Run(
            () =>
                scope
                    .ServiceProvider.GetRequiredService<ITripsReadiness>()
                    .IsReadyAsync(cancellation.Token),
            cancellation.Token
        );
        try
        {
            var ready = await check.WaitAsync(Budget, clock, request);
            request.ThrowIfCancellationRequested();
            return ready && clock.GetElapsedTime(started) < Budget;
        }
        catch (Exception)
        {
            request.ThrowIfCancellationRequested();
            return false;
        }
        finally
        {
            _ = FinishAsync(check, cancellation, scope);
        }
    }

    private async Task FinishAsync(
        Task check,
        CancellationTokenSource cancellation,
        AsyncServiceScope scope
    )
    {
        try
        {
            using var ownedCancellation = cancellation;
            await using var ownedScope = scope;
            // Keep the scope and its slot until both a late check and its cancellation callbacks exit.
            // A slow disposer also runs independently of the returning HTTP request.
            var stopping = check.IsCompleted ? Task.CompletedTask : cancellation.CancelAsync();
            await Task.WhenAll(check, stopping).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            logger.LogWarning("Optional Trips storage readiness check failed.");
        }
        finally
        {
            slots.Release();
        }
    }
}
