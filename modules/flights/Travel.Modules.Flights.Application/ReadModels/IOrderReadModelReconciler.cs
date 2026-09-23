namespace Travel.Modules.Flights.Application.ReadModels;

public enum OrderReadModelReconcileMode
{
    Incremental,
    Reset,
}

public sealed record ReconcileResult(
    Guid AggregateId,
    long? PreviousVersion,
    long SourceVersion,
    long? PersistedVersion,
    int AppliedEvents,
    bool Materialized
);

public sealed record ProjectionIssue(string Code, bool RepairableByReset);

public sealed record ProjectionValidation(
    Guid AggregateId,
    long? SourceVersion,
    long? PersistedVersion,
    bool WouldMaterialize,
    IReadOnlyList<ProjectionIssue> Issues
);

public interface IOrderReadModelReconciler
{
    Task<ReconcileResult> ReconcileAsync(
        Guid aggregateId,
        OrderReadModelReconcileMode mode,
        CancellationToken ct
    );

    Task<ProjectionValidation> ValidateAsync(Guid aggregateId, CancellationToken ct);
}
