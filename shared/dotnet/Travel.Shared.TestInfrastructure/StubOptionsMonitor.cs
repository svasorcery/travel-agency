using Microsoft.Extensions.Options;

namespace Travel.Shared.TestInfrastructure;

/// <summary>
/// Minimal <see cref="IOptionsMonitor{TOptions}"/> stub for use in unit and integration tests.
/// Always returns the same value; <see cref="OnChange"/> is a no-op.
/// </summary>
public sealed class StubOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue => value;

    public TOptions Get(string? name) => value;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
