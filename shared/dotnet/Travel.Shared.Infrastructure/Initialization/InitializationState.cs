namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>
/// The observable lifecycle of startup initialization.
/// </summary>
public enum InitializationState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}
