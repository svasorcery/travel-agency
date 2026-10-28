using Shouldly;
using Travel.Trips.Evaluation;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefEvaluationEnvironmentTests
{
    [Fact]
    public async Task Real_environment_default_preflight_has_no_provider_or_resource_work()
    {
        await using var environment = new TripsBriefEvaluationEnvironment();
        using var output = new StringWriter();
        var root = Root();
        (
            await TripsBriefEvaluationCommand.RunAsync(
                ["preflight", "--repo-root", root],
                environment,
                output,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(0);
        output.ToString().ShouldContain("\"ProviderCalls\":0");
        output.ToString().ShouldContain("\"ReadyForExecution\":false");
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
