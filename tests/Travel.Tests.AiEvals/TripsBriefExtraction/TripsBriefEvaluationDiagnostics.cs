namespace Travel.Tests.AiEvals.TripsBriefExtraction;

// Observations of normalized structured proposals; never raw provider safety/accuracy claims.
public sealed record TripsBriefEvaluationDiagnostics(
    string Observation,
    bool RejectedProposal,
    string[] NormalizerAddedIssueCodes,
    string[] DerivedSlots
);
