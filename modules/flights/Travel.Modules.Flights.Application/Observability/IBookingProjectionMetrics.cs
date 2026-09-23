namespace Travel.Modules.Flights.Application.Observability;

public interface IBookingProjectionMetrics
{
    void RecordReconcile(string outcome, int appliedEvents, double durationMs);
    void RecordProjectionFailure(string category);
    void RecordRebuild(string outcome);
    void RecordSourceCheckpointLag(long sourceVersion, long? checkpointVersion);
}
