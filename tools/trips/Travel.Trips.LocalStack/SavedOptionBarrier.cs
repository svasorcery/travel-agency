using System.Data.Common;
using System.IO.Pipes;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Travel.Modules.Trips.Infrastructure.Persistence;

namespace Travel.Trips.LocalStack;

// Only the disposable saved-profile Host installs this interceptor. The same-container pipe
// carries single-byte commands. No HTTP route, application setting or ticket can arm it.
public sealed class SavedOptionBarrier : DbTransactionInterceptor
{
    private readonly Lock gate = new();
    private TaskCompletionSource? arrived;
    private TaskCompletionSource? release;
    private bool claimed;

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (
            eventData.Context is not TripsDbContext db
            || !db
                .ChangeTracker.Entries<TripPlanEntity>()
                .Any(e => e.Property("ProtectedOption").CurrentValue is not null)
        )
            return result;
        Task wait;
        lock (gate)
        {
            if (arrived is null || release is null || claimed)
                return result;
            claimed = true;
            arrived.TrySetResult();
            wait = release.Task;
        }
        try
        {
            await wait.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
        }
        finally
        {
            lock (gate)
            {
                arrived = null;
                release = null;
                claimed = false;
            }
        }
        return result;
    }

    public async Task ServeAsync(string id, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                "trips-saved-" + id,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await server.WaitForConnectionAsync(stopping);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            byte[] message = new byte[1];
            try
            {
                await server.ReadExactlyAsync(message, timeout.Token);
                Task? pending = null;
                lock (gate)
                {
                    switch (message[0])
                    {
                        case 1 when arrived is null:
                            arrived = Signal();
                            release = Signal();
                            break;
                        case 2 when arrived is not null:
                            pending = arrived.Task;
                            break;
                        case 3 when release is not null:
                            release.TrySetResult();
                            break;
                        default:
                            throw new LocalStackRefusal("Invalid saved commit barrier transition.");
                    }
                }
                if (pending is not null)
                    await pending.WaitAsync(timeout.Token);
                message[0] = 1;
            }
            catch (Exception e)
                when (e is LocalStackRefusal or IOException or OperationCanceledException)
            {
                message[0] = 0;
            }
            try
            {
                await server.WriteAsync(message, stopping);
            }
            catch (IOException) { }
        }
    }

    internal static async Task ControlAsync(
        LocalStackState state,
        string action,
        CancellationToken ct
    )
    {
        if (state.Mode != "saved-spb-option" || state.E2eProfile != "saved-spb-option")
            throw new LocalStackRefusal("Requires the disposable saved profile.");
        byte command = action switch
        {
            "saved-arm" => 1,
            "saved-held" => 2,
            "saved-release" => 3,
            _ => throw new LocalStackRefusal("Unknown barrier command."),
        };
        await using var pipe = new NamedPipeClientStream(
            ".",
            "trips-saved-" + state.Id,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        await pipe.ConnectAsync(5000, ct);
        await pipe.WriteAsync(new[] { command }, ct);
        var reply = new byte[1];
        await pipe.ReadExactlyAsync(reply, ct);
        if (reply[0] != 1)
            throw new LocalStackRefusal("Saved commit barrier did not reach the requested state.");
    }
}
