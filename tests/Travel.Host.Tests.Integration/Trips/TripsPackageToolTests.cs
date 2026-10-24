using Shouldly;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Travel.Trips.LocalStack;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

public sealed class TripsPackageToolTests
{
    private static string PackageDirectory
    {
        get
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            return Path.Combine(
                root!.FullName,
                "modules",
                "trips",
                "Travel.Modules.Trips.Infrastructure",
                "Itineraries",
                "Data",
                "moscow-outdoor",
                "1"
            );
        }
    }

    [Fact]
    public async Task Package_check_accepts_reviewed_bytes_without_stack_state_or_provisioning()
    {
        (await ToolProgram.Main(Arguments("2026-10-24T12:00:00Z"))).ShouldBe(0);
        Directory.Exists(Path.Combine(PackageDirectory, "private")).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-11-17T03:55:13Z")]
    [InlineData("2026-10-20T00:00:00Z")]
    [InlineData("2026-10-24T15:00:00+03:00")]
    [InlineData("today")]
    public async Task Package_check_refuses_expired_prebuild_or_non_UTC_evaluation(string at) =>
        (await ToolProgram.Main(Arguments(at))).ShouldBe(1);

    [Fact]
    public async Task Package_check_cannot_repin_v1_or_accept_unknown_options()
    {
        var wrong = Arguments("2026-10-24T12:00:00Z");
        wrong[6] = new string('a', 64);
        (await ToolProgram.Main(wrong)).ShouldBe(1);
        (
            await ToolProgram.Main([.. Arguments("2026-10-24T12:00:00Z"), "--state", "foreign"])
        ).ShouldBe(1);
    }

    private static string[] Arguments(string at) =>
        [
            "package-check",
            "--directory",
            PackageDirectory,
            "--version",
            "1",
            "--manifest-sha",
            CuratedMoscowRelease.ReviewedV1ManifestSha256,
            "--as-of",
            at,
        ];

    [Fact]
    public async Task Package_check_refuses_missing_relative_and_corrupted_files()
    {
        var arguments = Arguments("2026-10-24T12:00:00Z");
        arguments[2] = "relative/package";
        (await ToolProgram.Main(arguments)).ShouldBe(1);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "travel-package-check-" + Guid.NewGuid().ToString("N")
        );
        arguments[2] = directory;
        (await ToolProgram.Main(arguments)).ShouldBe(1);
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in Directory.GetFiles(PackageDirectory))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            (await ToolProgram.Main(arguments)).ShouldBe(0);
            File.AppendAllText(Path.Combine(directory, "moscow-v1.json"), " ");
            (await ToolProgram.Main(arguments)).ShouldBe(1);
            File.Delete(Path.Combine(directory, "ATTRIBUTION.txt"));
            (await ToolProgram.Main(arguments)).ShouldBe(1);
        }
        finally
        {
            if (
                Path.GetDirectoryName(Path.GetFullPath(directory))
                    != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                || !Path.GetFileName(directory)
                    .StartsWith("travel-package-check-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("Unexpected fixture path.");
            Directory.Delete(directory, true);
        }
    }
}
