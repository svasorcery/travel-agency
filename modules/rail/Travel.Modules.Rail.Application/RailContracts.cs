using System.Collections.Immutable;
using ErrorOr;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed record RailProfile(
    string Epoch,
    string Name,
    bool Enabled,
    CancellationToken Revoked
);

public interface IRailRuntimeProfile
{
    RailProfile Current { get; }
}

public sealed record CatalogueData(
    ImmutableArray<RailStation> Stations,
    bool Limited,
    long PayloadBytes
);

public sealed record ReferenceData(
    CatalogueData Catalogue,
    RailAttribution Attribution,
    DateTimeOffset ReceivedAt
);

public interface IRailReferenceLoader
{
    Task<ErrorOr<ReferenceData>> LoadAsync(
        RailProfile profile,
        CancellationToken cancellationToken
    );
}

public sealed record ReferenceBundle(
    string Generation,
    string ProfileEpoch,
    string Profile,
    CatalogueData Catalogue,
    RailAttribution Attribution,
    DateTimeOffset ReceivedAt,
    DateTimeOffset ExpiresAt
);

public sealed record RailReferenceSelection(
    string Generation,
    string ProfileEpoch,
    RailStation From,
    RailStation To,
    RailAttribution Attribution,
    DateTimeOffset ExpiresAt
);

public sealed record ModeResult(
    string Transport,
    string Kind,
    ImmutableArray<string> Reasons,
    int OriginalCount,
    ImmutableArray<ScheduledJourney> Items,
    DateTimeOffset? ReceivedAt
)
{
    public int NormalizedCount => Items.Length;

    public static ModeResult Unavailable(string mode, string kind, string reason) =>
        new(mode, kind, [reason], 0, [], null);
}

public interface IRailScheduleProvider
{
    Task<ModeResult> SearchAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    );
}

public sealed record ModeEvidence(
    string Transport,
    string Kind,
    ImmutableArray<string> Reasons,
    int OriginalCount,
    int NormalizedCount,
    DateTimeOffset? ReceivedAt
);

public sealed record RailSearchResult(
    string Kind,
    ImmutableArray<string> Reasons,
    RailRequest Request,
    string Source,
    string Profile,
    string ProfileEpoch,
    ImmutableArray<ModeEvidence> Modes,
    ImmutableArray<ScheduledJourney> Items,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CompletedAt,
    RailAttribution? Attribution
)
{
    public int NormalizedCount => Items.Length;
}

public sealed record StationSuggestions(
    string Kind,
    ImmutableArray<string> Reasons,
    ImmutableArray<RailStation> Suggestions,
    bool HasMore,
    string? CatalogueGeneration,
    string Source,
    string Profile,
    string ProfileEpoch,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? ExpiresAt,
    RailAttribution? Attribution
);

public interface IRailSearchCache
{
    RailSearchResult? Get(string key, DateTimeOffset now);
    void Set(string key, RailSearchResult result);
    void Clear();
}
