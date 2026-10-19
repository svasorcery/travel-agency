using ErrorOr;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application;

// Owned by the shared cache, so scoped services cannot each create two more aggregate slots.
public sealed class HotelReadCoordinator(TimeProvider time)
{
    private readonly SemaphoreSlim requests = new(2, 2);

    public async Task<ErrorOr<HotelExecution<HotelOffer>[]>> RunAsync(
        Func<CancellationToken, Task<HotelExecution<HotelOffer>>>[] executions,
        CancellationToken caller
    )
    {
        if (executions.Length > 2)
            return HotelErrors.Invalid;
        if (!requests.Wait(0))
            return HotelErrors.Capacity;
        using var aggregate = new CancellationTokenSource(TimeSpan.FromSeconds(17), time);
        var lifetimes = new List<CancellationTokenSource>();
        var actual = new List<Task<HotelExecution<HotelOffer>>>();
        try
        {
            var observed = executions
                .Select(execute =>
                {
                    var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), time);
                    var child = CancellationTokenSource.CreateLinkedTokenSource(
                        caller,
                        deadline.Token,
                        aggregate.Token
                    );
                    lifetimes.Add(deadline);
                    lifetimes.Add(child);
                    Task<HotelExecution<HotelOffer>> task;
                    try
                    {
                        task = execute(child.Token);
                    }
                    catch (Exception ex)
                    {
                        task = Task.FromException<HotelExecution<HotelOffer>>(ex);
                    }
                    actual.Add(task);
                    return Observe(task, child.Token, caller);
                })
                .ToArray();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                caller,
                aggregate.Token
            );
            var results = await Task.WhenAll(observed).WaitAsync(linked.Token);
            caller.ThrowIfCancellationRequested();
            return results;
        }
        finally
        {
            foreach (var life in lifetimes)
                life.Cancel();
            var all = Task.WhenAll(actual);
            if (all.IsCompleted)
                Release();
            else
                _ = all.ContinueWith(
                    _ => Release(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            void Release()
            {
                _ = all.Exception; // Observe detached fault; never publish a late result.
                foreach (var life in lifetimes)
                    life.Dispose();
                requests.Release();
            }
        }
    }

    private static async Task<HotelExecution<HotelOffer>> Observe(
        Task<HotelExecution<HotelOffer>> actual,
        CancellationToken deadline,
        CancellationToken caller
    )
    {
        try
        {
            var result = await actual.WaitAsync(deadline);
            deadline.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            caller.ThrowIfCancellationRequested();
            return HotelExecution<HotelOffer>.Failed("Timeout");
        }
        catch (HttpRequestException)
        {
            return HotelExecution<HotelOffer>.Failed("Transport");
        }
    }
}
