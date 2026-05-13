namespace Travel.Shared.Abstractions;

/// <summary>
/// Marks a class as test-only / sandbox-only. ArchUnit tests forbid registration
/// of [TestOnly] classes in production DI configurations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TestOnlyAttribute : Attribute;
