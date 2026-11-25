using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Travel.Trips.LocalStack;

// Count actual outgoing HttpClient activities during each preview. Emit only the count.
internal sealed class OptionHttpObservation : IStartupFilter, IDisposable
{
    private sealed class Counter
    {
        internal int Value;
    }

    private readonly AsyncLocal<Counter?> current = new();
    private readonly ActivityListener listener;

    public OptionHttpObservation()
    {
        listener = new()
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                current.Value is null
                    ? ActivitySamplingResult.None
                    : ActivitySamplingResult.AllData,
            ActivityStarted = _ =>
            {
                if (current.Value is { } counter)
                    Interlocked.Increment(ref counter.Value);
            },
        };
        ActivitySource.AddActivityListener(listener);
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                async (context, advance) =>
                {
                    if (
                        context.Request.Path != "/api/trips/option-preview"
                        || context.Request.Method != "POST"
                    )
                    {
                        await advance();
                        return;
                    }
                    var counter = new Counter();
                    current.Value = counter;
                    try
                    {
                        await advance();
                    }
                    finally
                    {
                        current.Value = null;
                        Console.WriteLine(
                            $"TRIPS_LOCAL_OPTION_HTTP={Volatile.Read(ref counter.Value)}"
                        );
                    }
                }
            );
            next(app);
        };

    public void Dispose() => listener.Dispose();
}
