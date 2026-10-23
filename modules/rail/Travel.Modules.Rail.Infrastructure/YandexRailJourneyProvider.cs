using System.Collections.Immutable;
using System.Text.Json;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using static Travel.Modules.Rail.Infrastructure.YandexRailScheduleParser;

namespace Travel.Modules.Rail.Infrastructure;

public sealed class YandexRailJourneyProvider(YandexRailClient client, TimeProvider clock)
    : IRailJourneyVerificationProvider,
        IRailJourneyRouteProvider
{
    public async Task<RailVerificationPage> VerifyAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        var query =
            $"&system=yandex&lang=ru_RU&transfers=false&from={Uri.EscapeDataString(request.From.Code)}&to={Uri.EscapeDataString(request.To.Code)}&date={Uri.EscapeDataString(request.DepartureDate)}&transport_types={mode}&offset=0&limit=50";
        var result = await client.GetAsync("search", query, profile, cancellationToken);
        if (result.IsError)
            return new(
                ModeResult.Unavailable(
                    mode,
                    result.FirstError.Code is "quota" or "source-disabled" ? "skipped" : "failed",
                    result.FirstError.Code
                ),
                []
            );
        using var response = result.Value;
        var body = await YandexRailClient.ReadBoundedAsync(
            response,
            2 * 1024 * 1024,
            cancellationToken
        );
        if (body.IsError)
            return new(ModeResult.Unavailable(mode, "failed", body.FirstError.Code), []);
        var received = clock.GetUtcNow();
        var page = YandexRailScheduleParser.Parse(body.Value, request, mode, received);
        if (page.Kind is not ("complete" or "limited"))
            return new(page, []);
        using var document = Read(body.Value, 2 * 1024 * 1024);
        var facts = ImmutableArray.CreateBuilder<RailLocatorFact>();
        foreach (var row in One(document.RootElement, "segments").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uid = Text(One(row, "thread"), "uid");
            var departure = ParseTime(Text(row, "departure"));
            var arrival = ParseTime(Text(row, "arrival"));
            var journey = page.Items.Single(x =>
                x.ThreadReference == uid
                && RailRouteRules.SameTime(x.Departure, departure)
                && RailRouteRules.SameTime(x.Arrival, arrival)
            );
            try
            {
                var value = One(One(row, "thread"), "thread_method_link");
                facts.Add(
                    YandexRailRouteLocatorParser.Parse(
                        journey.Key,
                        uid,
                        value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    )
                );
            }
            catch (JsonException)
            {
                facts.Add(new(journey.Key, "invalid", null));
            }
        }
        return new(page, facts.ToImmutable());
    }

    public async Task<RailRouteResult> RouteAsync(
        ScheduledJourney journey,
        RailRouteLocator locator,
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (locator.Source != "yandex-rasp" || locator.Uid != journey.ThreadReference)
            return RailRouteResult.Unavailable("failed", "invalid-route-locator");
        var query =
            $"&lang=ru_RU&uid={Uri.EscapeDataString(locator.Uid)}&from={Uri.EscapeDataString(journey.From.Code)}&to={Uri.EscapeDataString(journey.To.Code)}&date={Uri.EscapeDataString(locator.SourceLookupDate)}";
        var result = await client.GetAsync("thread", query, profile, cancellationToken);
        if (result.IsError)
            return RailRouteResult.Unavailable(
                result.FirstError.Code is "quota" or "source-disabled" or "details-disabled"
                    ? "skipped"
                    : "failed",
                result.FirstError.Code == "headers-oversize"
                    ? "output-limit"
                    : result.FirstError.Code
            );
        using var response = result.Value;
        var body = await YandexRailClient.ReadBoundedAsync(
            response,
            2 * 1024 * 1024,
            cancellationToken
        );
        if (body.IsError)
            return RailRouteResult.Unavailable("failed", "output-limit");
        var received = clock.GetUtcNow();
        return YandexRailRouteParser.Parse(body.Value, journey, received, cancellationToken);
    }
}

// Dispatch is explicit. A real failure never selects fictional transport.
public sealed class RailJourneyTransport
    : IRailJourneyVerificationProvider,
        IRailJourneyRouteProvider
{
    private readonly YandexRailJourneyProvider real;
    private readonly YandexRailJourneyProvider? fictional;

    public RailJourneyTransport(
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

    private YandexRailJourneyProvider? Select(RailProfile profile) =>
        profile.Name == "real" ? real
        : profile.Name == "fictional" ? fictional
        : null;

    public Task<RailVerificationPage> VerifyAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    ) =>
        Select(profile)?.VerifyAsync(request, mode, profile, cancellationToken)
        ?? Task.FromResult(
            new RailVerificationPage(ModeResult.Unavailable(mode, "skipped", "source-disabled"), [])
        );

    public Task<RailRouteResult> RouteAsync(
        ScheduledJourney journey,
        RailRouteLocator locator,
        RailProfile profile,
        CancellationToken cancellationToken
    ) =>
        Select(profile)?.RouteAsync(journey, locator, profile, cancellationToken)
        ?? Task.FromResult(RailRouteResult.Unavailable("skipped", "source-disabled"));
}
