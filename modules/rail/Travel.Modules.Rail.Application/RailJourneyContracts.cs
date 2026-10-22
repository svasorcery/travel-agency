using System.Collections.Immutable;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed record RailJourneySelection(
    string Key,
    string Transport,
    DateTimeOffset Departure,
    DateTimeOffset Arrival
);

public sealed record RailJourneyDetailsRequest(
    RailRequest Search,
    string ExpectedProfileEpoch,
    RailJourneySelection Selection
);

public sealed record RailRouteLocator(string Source, string Uid, string SourceLookupDate);

public sealed record RailLocatorFact(string Key, string State, RailRouteLocator? Locator);

public sealed record RailVerificationPage(
    ModeResult Page,
    ImmutableArray<RailLocatorFact> Locators
);

public interface IRailJourneyVerificationProvider
{
    Task<RailVerificationPage> VerifyAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    );
}

public sealed record RailRouteResult(
    string Kind,
    ImmutableArray<string> Reasons,
    ImmutableArray<RailRouteStop> Stops,
    int? BoardingIndex,
    int? AlightingIndex,
    DateTimeOffset? ReceivedAt
)
{
    public static RailRouteResult Unavailable(
        string kind,
        string reason,
        DateTimeOffset? receivedAt = null
    ) => new(kind, [reason], [], null, null, receivedAt);
}

public interface IRailJourneyRouteProvider
{
    Task<RailRouteResult> RouteAsync(
        ScheduledJourney journey,
        RailRouteLocator locator,
        RailProfile profile,
        CancellationToken cancellationToken
    );
}

public sealed record RailVerificationEvidence(
    string Transport,
    string Kind,
    ImmutableArray<string> Reasons,
    DateTimeOffset? ReceivedAt
);

public sealed record RailJourneyDetailsResult(
    string Kind,
    ImmutableArray<string> Reasons,
    string Stage,
    RailJourneyDetailsRequest Request,
    string Source,
    string Profile,
    string ProfileEpoch,
    RailVerificationEvidence? Verification,
    DateTimeOffset? RouteReceivedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CompletedAt,
    ImmutableArray<RailRouteStop> Stops,
    int? BoardingIndex,
    int? AlightingIndex,
    RailAttribution? Attribution
);
