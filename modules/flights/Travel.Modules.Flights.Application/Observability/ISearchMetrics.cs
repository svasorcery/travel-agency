namespace Travel.Modules.Flights.Application.Observability;

public interface ISearchMetrics
{
    void RecordSearchLatency(double elapsedMs, string provider, string status);
}
