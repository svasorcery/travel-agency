using Shouldly;
using Travel.Trips.Evaluation;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefEvaluationEnvironmentTests
{
    [Fact]
    public async Task Generated_web_metadata_is_bound_but_unknown_or_nested_files_stay_rejected()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "trips-build-closure-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(root, "Travel.Trips.Evaluation.dll"),
                [1, 2, 3],
                TestContext.Current.CancellationToken
            );
            var metadata = Path.Combine(root, "Travel.AI.staticwebassets.endpoints.json");
            await File.WriteAllTextAsync(metadata, "{}", TestContext.Current.CancellationToken);
            var first = TripsBriefEvaluationEnvironment.InspectBuildClosure(root);
            first.IsClean.ShouldBeTrue();
            await File.WriteAllTextAsync(
                metadata,
                "{\"changed\":true}",
                TestContext.Current.CancellationToken
            );
            var changed = TripsBriefEvaluationEnvironment.InspectBuildClosure(root);
            changed.IsClean.ShouldBeTrue();
            changed.Hash.ShouldNotBe(first.Hash);
            var unknown = Path.Combine(root, "unexpected.json");
            await File.WriteAllTextAsync(unknown, "{}", TestContext.Current.CancellationToken);
            TripsBriefEvaluationEnvironment.InspectBuildClosure(root).IsClean.ShouldBeFalse();
            File.Delete(unknown);
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(
                Path.Combine(nested, Path.GetFileName(metadata)),
                "{}",
                TestContext.Current.CancellationToken
            );
            TripsBriefEvaluationEnvironment.InspectBuildClosure(root).IsClean.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

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
