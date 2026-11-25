using ErrorOr;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;

namespace Travel.Modules.Trips.Application.Options;

/// <summary>The snapshot instant belongs only to the fictional catalogue and walking evidence.</summary>
public sealed class TripOptionScenario(string catalogueVersion, DateTimeOffset asOfUtc)
    : TimeProvider
{
    public static readonly DateTimeOffset HistoricalInstant = new(
        2026,
        10,
        27,
        12,
        0,
        0,
        TimeSpan.Zero
    );
    public string CatalogueVersion { get; } = catalogueVersion;

    public override DateTimeOffset GetUtcNow() => asOfUtc;
}

public sealed class TripOptionService(
    TripOptionScenario scenario,
    IItineraryFactSource historicalSource,
    ItineraryAdmissionGate admission,
    Func<bool> withdrawn
)
{
    public async Task<ErrorOr<TripOptionPreview>> PreviewAsync(
        TripBriefInput input,
        CancellationToken ct
    )
    {
        var brief = TripBrief.Create(input);
        if (brief.IsError || brief.Value.OriginId is null)
            return TripErrors.InvalidInput;
        ct.ThrowIfCancellationRequested();
        var asOf = scenario.GetUtcNow();
        if (OptionPlanner.Unsupported(brief.Value, asOf, scenario.CatalogueVersion) is { } outside)
            return outside;
        if (!admission.TryEnter())
            return Error.Failure("Trips.ItineraryBusy", "Itinerary generation is busy.");
        try
        {
            using var deadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(2),
                TimeProvider.System
            );
            using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            try
            {
                var token = request.Token;
                token.ThrowIfCancellationRequested();
                var destination = CityCatalogue.Find("saint-petersburg").Value;
                var facts = await historicalSource.ReadAsync(destination, token);
                token.ThrowIfCancellationRequested();
                if (
                    facts.IsError
                    || withdrawn()
                    || asOf < facts.Value.BuildAsOfUtc
                    || asOf >= facts.Value.ReleaseValidUntil
                    || asOf >= facts.Value.ModelSource.ValidUntil
                )
                    return Unavailable();
                var result = OptionPlanner.Preview(
                    brief.Value,
                    facts.Value,
                    asOf,
                    FictionalOptionCatalogue.For(scenario.CatalogueVersion, brief.Value.Adults),
                    token
                );
                token.ThrowIfCancellationRequested();
                if (withdrawn() || scenario.GetUtcNow() >= facts.Value.ReleaseValidUntil)
                    return Unavailable();
                return result;
            }
            catch (OperationCanceledException)
                when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                return Error.Failure("Trips.ItineraryDeadline", "Preview deadline exceeded.");
            }
        }
        finally
        {
            admission.Exit();
        }
    }

    private static Error Unavailable() =>
        Error.Failure("Trips.OptionUnavailable", "Preview evidence is unavailable.");
}
