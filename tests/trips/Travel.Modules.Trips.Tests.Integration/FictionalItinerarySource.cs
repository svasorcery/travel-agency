using ErrorOr;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Tests.Integration;

internal sealed class FictionalItinerarySource : IItineraryFactSource
{
    internal ValidatedItineraryFacts Facts { get; set; } = FictionalItineraryFacts.Create();
    internal bool Unavailable { get; set; }
    internal int Calls { get; private set; }
    internal Action? OnRead { get; set; }

    public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        OnRead?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ErrorOr<ValidatedItineraryFacts>>(
            Unavailable ? Error.Failure("Trips.ItineraryUnavailable", "Unavailable.") : Facts
        );
    }

    internal void CloseAll()
    {
        var input = FictionalItineraryFacts.Input();
        var evidence = input
            .Evidence.Concat(
                input.Pois.Select(p =>
                    FictionalItineraryFacts
                        .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                        .Build()
                        .Value
                )
            )
            .ToArray();
        var pois = input
            .Pois.Select(p =>
                FictionalItineraryFacts.Poi(
                    p.Id,
                    access: ItineraryAccess.Create(p.Id, true, "access-" + p.Id, [], []).Value
                )
            )
            .ToArray();
        Facts = (input with { Pois = pois, Evidence = evidence }).Build().Value;
    }
}

internal sealed class ItineraryHttpClock : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = FictionalItineraryFacts.Now;
    internal Func<DateTimeOffset>? Read { get; set; }

    public override DateTimeOffset GetUtcNow() => Read?.Invoke() ?? Now;
}
