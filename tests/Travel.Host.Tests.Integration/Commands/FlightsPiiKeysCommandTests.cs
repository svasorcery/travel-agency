using Microsoft.Extensions.Configuration;
using Shouldly;
using Travel.Host.Commands;
using Travel.Tests.Fixtures;

namespace Travel.Host.Tests.Integration.Commands;

public sealed class FlightsPiiKeysCommandTests
{
    [Fact]
    public async Task Initialize_requires_execute_and_never_replaces_an_initialized_ring()
    {
        using var ring = new FlightPiiTestRing(false);
        var values = ring.Configuration;
        values["ConnectionStrings:travel"] = "deliberately-invalid-no-database-may-be-used";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var output = new StringWriter();
        (await FlightsPiiKeysCommand.RunAsync(["initialize"], output, config)).ShouldBe(2);
        Directory.Exists(ring.Options.KeyRingPath).ShouldBeFalse();
        (
            await FlightsPiiKeysCommand.RunAsync(["initialize", "--execute"], output, config)
        ).ShouldBe(0);
        var keyFiles = Directory.GetFiles(ring.Options.KeyRingPath!, "*.xml");
        keyFiles.Length.ShouldBe(1);
        (
            await FlightsPiiKeysCommand.RunAsync(["initialize", "--execute"], output, config)
        ).ShouldBe(3);
        Directory.GetFiles(ring.Options.KeyRingPath!, "*.xml").ShouldBe(keyFiles);
        output.ToString().ShouldNotContain(ring.Options.ActiveCertificatePassword!);
        output.ToString().ShouldNotContain(ring.Root);
    }
}
