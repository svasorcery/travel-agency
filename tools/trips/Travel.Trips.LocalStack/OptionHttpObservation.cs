using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Travel.Modules.Trips.Application.Itineraries;

namespace Travel.Trips.LocalStack;

// Count actual outgoing HttpClient activities during each preview. Emit only the count.
internal sealed class OptionHttpObservation : IStartupFilter, IDisposable
{
    private sealed class Counter
    {
        internal int Value;
        internal long Previews;
        internal long PackageReads;
    }

    private readonly AsyncLocal<Counter?> current = new();
    private readonly ActivityListener listener;
    private readonly MeterListener measurements = new();

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
        measurements.InstrumentPublished = (instrument, observer) =>
        {
            if (
                instrument.Meter.Name == TripItineraryMeasurements.MeterName
                && instrument.Name
                    is TripItineraryMeasurements.PreviewName
                        or TripItineraryMeasurements.PackageReadName
            )
                observer.EnableMeasurementEvents(instrument);
        };
        measurements.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) =>
            {
                if (current.Value is not { } counter)
                    return;
                if (instrument.Name == TripItineraryMeasurements.PreviewName)
                    Interlocked.Add(ref counter.Previews, value);
                else
                    Interlocked.Add(ref counter.PackageReads, value);
            }
        );
        measurements.Start();
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                async (context, advance) =>
                {
                    var preview =
                        context.Request.Path == "/api/trips/option-preview"
                        && context.Request.Method == "POST";
                    var historical =
                        context.Request.Path.Value?.StartsWith(
                            "/api/trips/",
                            StringComparison.Ordinal
                        ) == true
                        && context.Request.Path.Value.EndsWith(
                            "/saved-option",
                            StringComparison.Ordinal
                        )
                        && context.Request.Method == "GET";
                    var save =
                        context.Request.Path == "/api/trips/saved-options"
                        && context.Request.Method == "POST";
                    if (!preview && !historical && !save)
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
                        if (preview)
                            Console.WriteLine(
                                $"TRIPS_LOCAL_OPTION_HTTP={Volatile.Read(ref counter.Value)}"
                            );
                        var kind =
                            preview ? "preview"
                            : save ? "save"
                            : "historical";
                        Console.WriteLine(
                            $"TRIPS_LOCAL_SAVED_REQUEST={kind},{context.Response.StatusCode},{Volatile.Read(ref counter.Value)},{Volatile.Read(ref counter.Previews)},{Volatile.Read(ref counter.PackageReads)}"
                        );
                    }
                }
            );
            next(app);
        };

    public void Dispose()
    {
        listener.Dispose();
        measurements.Dispose();
    }
}
