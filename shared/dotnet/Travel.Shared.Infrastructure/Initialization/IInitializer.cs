namespace Travel.Shared.Infrastructure.Initialization;

/// Implemented by per-module bootstrap logic (Marten schema apply, EF migrate,
/// realm import, projection warm-up). Discovered and executed once at host
/// startup by AppInitializer hosted service. Order is not guaranteed —
/// initializers must be independent.
public interface IInitializer
{
    Task InitializeAsync(CancellationToken ct);
}
