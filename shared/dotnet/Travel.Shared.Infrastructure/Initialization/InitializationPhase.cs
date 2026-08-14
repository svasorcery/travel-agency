namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>
/// Ordered startup stages for module-owned initialization work.
/// </summary>
public enum InitializationPhase
{
    Platform,
    RelationalSchema,
    EventStoreSchema,
    DevelopmentSeed,
}
