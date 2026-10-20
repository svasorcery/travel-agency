using ErrorOr;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Infrastructure;

// The selected profile is explicit; real errors never fall back to fictional data.
public sealed class RailReferenceTransport : IRailReferenceLoader
{
    private readonly YandexRailReferenceLoader real;
    private readonly YandexRailReferenceLoader? fictional;

    public RailReferenceTransport(
        YandexRailClient client,
        RailRuntime runtime,
        FictionalRailTransport? transport,
        RailQuotaLedger quota,
        TimeProvider clock
    )
    {
        real = new(client, clock);
        if (transport is not null)
            fictional = new(
                new YandexRailClient(new HttpClient(transport, false), runtime, quota, clock),
                clock
            );
    }

    public async Task<ErrorOr<ReferenceData>> LoadAsync(
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (profile.Name == "real")
            return await real.LoadAsync(profile, cancellationToken);
        if (fictional is null)
            return Error.Failure("source-disabled");
        var result = await fictional.LoadAsync(profile, cancellationToken);
        return result.IsError
            ? result.Errors
            : result.Value with
            {
                Attribution = result.Value.Attribution with
                {
                    FrameUrl = "/rail-fictional-banner.html",
                },
            };
    }
}

public sealed class RailScheduleTransport : IRailScheduleProvider
{
    private readonly YandexRailScheduleProvider real;
    private readonly YandexRailScheduleProvider? fictional;

    public RailScheduleTransport(
        YandexRailClient client,
        RailRuntime runtime,
        FictionalRailTransport? transport,
        RailQuotaLedger quota,
        TimeProvider clock
    )
    {
        real = new(client, clock);
        if (transport is not null)
            fictional = new(
                new YandexRailClient(new HttpClient(transport, false), runtime, quota, clock),
                clock
            );
    }

    public Task<ModeResult> SearchAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    ) =>
        profile.Name == "real" ? real.SearchAsync(request, mode, profile, cancellationToken)
        : fictional is not null ? fictional.SearchAsync(request, mode, profile, cancellationToken)
        : Task.FromResult(ModeResult.Unavailable(mode, "skipped", "source-disabled"));
}
