namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>
/// Implemented by per-module bootstrap logic and run once at host startup by
/// <see cref="AppInitializer"/>.
/// </summary>
public interface IInitializer
{
    /// <summary>
    /// Determines when this initializer runs relative to other initializers.
    /// </summary>
    InitializationPhase Phase { get; }

    Task InitializeAsync(CancellationToken ct);
}
