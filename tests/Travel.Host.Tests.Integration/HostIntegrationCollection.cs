using Xunit;

namespace Travel.Host.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostIntegrationCollection
{
    public const string Name = "Travel Host integration";
}
