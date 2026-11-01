using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Hotels.Tests.Integration;

// Only owned fixture wiring sees this header; nothing is added to product/provider DTOs.
public sealed class HotelM2LoadControl(HotelM2LoadProfile profile, TimeProvider clock)
{
    public const string Header = "X-Travel-Fictional-Job";
    public HotelM2LoadSchedule Schedule { get; } = new(profile);
    private readonly AsyncLocal<int?> current = new();
    private readonly ConcurrentDictionary<string, int> origins = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> readAttempts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string, int, string, int), byte> actual = new();
    private int started;
    public bool Active => Volatile.Read(ref started) != 0;
    private string? failure;
    public string? Failure => Volatile.Read(ref failure);

    public void Begin()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("Load clock already started.");
    }

    public void Fail(string reason)
    {
        Interlocked.CompareExchange(ref failure, reason, null);
        throw new InvalidOperationException("Owned load fixture invariant failed: " + reason);
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (
            !Active
            || !(
                context.Request.Path.StartsWithSegments("/api/hotels")
                || context.Request.Path == "/webhooks/duffel"
            )
        )
        {
            await next(context);
            return;
        }
        if (
            !int.TryParse(context.Request.Headers[Header].ToString(), out var id)
            || id < 0
            || id >= Schedule.Jobs.Count
        )
            Fail("MissingPlannedJob");
        var job = Schedule.Jobs[id];
        var expectedMethod =
            job.Kind == "status" ? "GET"
            : job.Kind == "prepare" ? "PUT"
            : "POST";
        var expectedPath = job.Kind switch
        {
            "search" => "/api/hotels/search",
            "rates" => "/api/hotels/rates",
            "prepare" => $"/api/hotels/reservations/{job.ReservationId}/quote",
            "create" => $"/api/hotels/reservations/{job.ReservationId}/purchase",
            "status" => $"/api/hotels/reservations/{job.ReservationId}",
            _ => "/webhooks/duffel",
        };
        if (context.Request.Method != expectedMethod || context.Request.Path != expectedPath)
            Fail("PlannedRouteMismatch");
        var old = current.Value;
        current.Value = id;
        try
        {
            await next(context);
        }
        finally
        {
            current.Value = old;
        }
    }

    public HotelM2PlannedStep? Resolve(
        string provider,
        string operation,
        HttpRequestMessage request
    )
    {
        if (!Active)
            return null;
        int id;
        var read = -1;
        if (operation == "read")
        {
            var key = provider + ":" + request.RequestUri!.AbsolutePath.Split('/')[^1];
            if (!origins.TryGetValue(key, out id))
            {
                Fail("UnplannedReferenceRead");
                return null;
            }
            read = readAttempts.AddOrUpdate(key, 0, (_, previous) => previous + 1);
        }
        else if (current.Value is { } value)
            id = value;
        else
        {
            Fail("UnplannedSourceCall");
            return null;
        }
        var step = Schedule.Steps.SingleOrDefault(s =>
            s.Provider == provider
            && s.JobId == id
            && s.Operation == operation
            && s.ReadAttempt == read
        );
        if (step is null || !actual.TryAdd((provider, id, operation, read), 0))
        {
            Fail("UnplannedOrRepeatedSourceStep");
            return null;
        }
        return step;
    }

    public async Task<HotelM2WireFault?> BeforeAsync(HotelM2PlannedStep? step, CancellationToken ct)
    {
        if (step is null)
            return null;
        var fault = Schedule.Fault(step);
        await Task.Delay(
            TimeSpan.FromMilliseconds(fault.DelayMilliseconds),
            clock,
            fault.IgnoreCancellation ? CancellationToken.None : ct
        );
        return fault;
    }

    public void RegisterCurrentBooking(string provider, string reference)
    {
        if (Active && current.Value is { } id)
            RegisterBooking(provider, reference, id);
    }

    public void RegisterBooking(string provider, string reference, int jobId)
    {
        if (origins.Count >= 128 || !origins.TryAdd(provider + ":" + reference, jobId))
            Fail("BookingOriginEnvelope");
    }

    public bool ReturnReferenceOnly(string provider) =>
        Active
        && provider == "hotelbeds"
        && current.Value is { } id
        && Schedule.Jobs[id].Kind == "create"
        && Schedule.Jobs[id].Due != 154.5;
}

public sealed class HotelM2LoadStartupFilter(HotelM2LoadControl control) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, continuation) => control.InvokeAsync(context, _ => continuation()));
            next(app);
        };
}
