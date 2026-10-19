namespace Travel.Modules.Hotels.Core;

public static class HotelAggregation
{
    public static ExecutionKind Kind(IReadOnlyCollection<ProviderStatus> sources) =>
        sources.Count == 0 || sources.All(s => s.Kind == ExecutionKind.Unsupported)
            ? ExecutionKind.Unsupported
        : sources.All(s => s.Kind == ExecutionKind.Complete) ? ExecutionKind.Complete
        : sources.Any(s => s.Kind is ExecutionKind.Complete or ExecutionKind.Limited)
            ? ExecutionKind.Limited
        : ExecutionKind.Failed;
}
