namespace Travel.Modules.Trips.Core.Itineraries;

public enum ItineraryOutcomeKind
{
    Candidate,
    Empty,
    Unsupported,
}

public enum ItineraryCoverage
{
    Filled,
    Partial,
}

public enum ItineraryFactState
{
    Known,
    Unknown,
}

// Immutable result values. Cross-field validity is independently checked at the consumer boundary.
// Public constructors also let validator tests represent corrupt output without reflection.
public sealed class ItineraryCandidateFact(
    ItineraryFactKind kind,
    ItineraryFactState state,
    string? evidenceId,
    string? unknownReason
)
{
    public ItineraryFactKind Kind { get; } = kind;
    public ItineraryFactState State { get; } = state;
    public string? EvidenceId { get; } = evidenceId;
    public string? UnknownReason { get; } = unknownReason;
}

public sealed class ItineraryCandidateBlock
{
    public string PoiId { get; }
    public Guid ProposedBlockId { get; }
    public string Title { get; }
    public DateOnly Date { get; }
    public string? StartTime { get; }
    public string? EndTime { get; }
    public PlanningMoney? Estimate { get; }
    public string Note { get; }
    public IReadOnlyList<ItineraryCandidateFact> Facts { get; }
    public IReadOnlyList<ItineraryWeeklyInterval> OpeningIntervals { get; }

    public ItineraryCandidateBlock(
        string poiId,
        Guid proposedBlockId,
        string title,
        DateOnly date,
        string? startTime,
        string? endTime,
        PlanningMoney? estimate,
        string note,
        IReadOnlyList<ItineraryCandidateFact> facts,
        IReadOnlyList<ItineraryWeeklyInterval> openingIntervals
    )
    {
        PoiId = poiId;
        ProposedBlockId = proposedBlockId;
        Title = title;
        Date = date;
        StartTime = startTime;
        EndTime = endTime;
        Estimate = estimate;
        Note = note;
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(openingIntervals);
        Facts = Array.AsReadOnly(facts.ToArray());
        OpeningIntervals = Array.AsReadOnly(openingIntervals.ToArray());
    }
}

public sealed class ItineraryCandidateEdge(
    string edgeId,
    string fromPoiId,
    string toPoiId,
    int minimumMinutes,
    int maximumMinutes,
    string evidenceId,
    string restrictions,
    ItineraryFactMethod method,
    ItineraryRightsBasis rightsBasis,
    ItineraryModelledWalk modelledWalk
)
{
    public string EdgeId { get; } = edgeId;
    public string FromPoiId { get; } = fromPoiId;
    public string ToPoiId { get; } = toPoiId;
    public int MinimumMinutes { get; } = minimumMinutes;
    public int MaximumMinutes { get; } = maximumMinutes;
    public string EvidenceId { get; } = evidenceId;
    public string Restrictions { get; } = restrictions;
    public ItineraryFactMethod Method { get; } = method;
    public ItineraryRightsBasis RightsBasis { get; } = rightsBasis;
    public ItineraryModelledWalk ModelledWalk { get; } = modelledWalk;
}

public sealed class ItineraryCandidateDay
{
    public DateOnly Date { get; }
    public IReadOnlyList<ItineraryCandidateBlock> Blocks { get; }
    public IReadOnlyList<ItineraryCandidateEdge> Edges { get; }

    public ItineraryCandidateDay(
        DateOnly date,
        IReadOnlyList<ItineraryCandidateBlock> blocks,
        IReadOnlyList<ItineraryCandidateEdge> edges
    )
    {
        Date = date;
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(edges);
        Blocks = Array.AsReadOnly(blocks.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
    }
}

public sealed class ItineraryOutcome
{
    public ItineraryOutcomeKind Kind { get; }
    public DestinationSelection Destination { get; }
    public string DatasetId { get; }
    public string DatasetVersion { get; }
    public string DatasetHash { get; }
    public string PolicyVersion { get; }
    public DateTimeOffset GeneratedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ItineraryDemoPreparation Preparation { get; }
    public ItineraryModelSource ModelSource { get; }
    public IReadOnlyList<ItineraryCandidateDay> Days { get; }
    public ItineraryCoverage Coverage { get; }
    public IReadOnlyList<string> CoveredInterests { get; }
    public IReadOnlyList<string> UnmetInterests { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<string> Exclusions { get; }
    public IReadOnlyList<string> Uncertainties { get; }
    public bool SearchLimited { get; }
    public int ExpansionCount { get; }

    public ItineraryOutcome(
        ItineraryOutcomeKind kind,
        DestinationSelection destination,
        string datasetId,
        string datasetVersion,
        string datasetHash,
        string policyVersion,
        DateTimeOffset generatedAt,
        DateTimeOffset expiresAt,
        ItineraryDemoPreparation preparation,
        ItineraryModelSource modelSource,
        IReadOnlyList<ItineraryCandidateDay> days,
        ItineraryCoverage coverage,
        IReadOnlyList<string> coveredInterests,
        IReadOnlyList<string> unmetInterests,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> exclusions,
        IReadOnlyList<string> uncertainties,
        bool searchLimited,
        int expansionCount
    )
    {
        Kind = kind;
        Destination = destination;
        DatasetId = datasetId;
        DatasetVersion = datasetVersion;
        DatasetHash = datasetHash;
        PolicyVersion = policyVersion;
        GeneratedAt = generatedAt;
        ExpiresAt = expiresAt;
        Preparation = preparation;
        ModelSource = modelSource;
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(coveredInterests);
        ArgumentNullException.ThrowIfNull(unmetInterests);
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(exclusions);
        ArgumentNullException.ThrowIfNull(uncertainties);
        Days = Array.AsReadOnly(days.ToArray());
        Coverage = coverage;
        CoveredInterests = Array.AsReadOnly(coveredInterests.ToArray());
        UnmetInterests = Array.AsReadOnly(unmetInterests.ToArray());
        Reasons = Array.AsReadOnly(reasons.ToArray());
        Exclusions = Array.AsReadOnly(exclusions.ToArray());
        Uncertainties = Array.AsReadOnly(uncertainties.ToArray());
        SearchLimited = searchLimited;
        ExpansionCount = expansionCount;
    }
}
