using System.Diagnostics;
using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelManifestReaderTests
{
    [Fact]
    public void M2_manifest_filename_is_resolved_against_its_selected_repository()
    {
        using var fixture = new ManifestFixture();
        HotelsSourceManifest.VerifyFile(
            fixture.Root,
            ManifestFixture.Output,
            [ManifestFixture.Profile]
        );
    }

    [Fact]
    public void M2_manifest_cannot_be_redirected_to_a_different_evidence_tree()
    {
        using var fixture = new ManifestFixture();
        var redirected = fixture.RedirectManifestToM1();
        Should.Throw<InvalidOperationException>(() =>
            HotelsSourceManifest.VerifyFile(fixture.Root, redirected, [ManifestFixture.Profile])
        );
    }

    [Fact]
    public void M2_writer_output_is_accepted_without_fabricating_legacy_M1_profiles()
    {
        using var fixture = new ManifestFixture();
        HotelsSourceManifest.Verify(fixture.Root, fixture.Manifest(), [ManifestFixture.Profile]);
        File.AppendAllText(Path.Combine(fixture.Root, ManifestFixture.Profile), "changed");
        Should.Throw<InvalidOperationException>(() =>
            HotelsSourceManifest.Verify(fixture.Root, fixture.Manifest(), [ManifestFixture.Profile])
        );
    }

    [Fact]
    public void M2_writer_and_reader_accept_the_exact_archive_profile()
    {
        const string profile =
            "openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance/load-plan.md";
        using var fixture = new ManifestFixture(profile);
        HotelsSourceManifest.VerifyFile(fixture.Root, ManifestFixture.Output, [profile]);
        File.AppendAllText(Path.Combine(fixture.Root, profile), "changed");
        Should.Throw<InvalidOperationException>(() =>
            HotelsSourceManifest.VerifyFile(fixture.Root, ManifestFixture.Output, [profile])
        );
    }

    [Theory]
    [InlineData("openspec/changes/archive/2026-11-01-other/load-plan.md")]
    [InlineData(
        "openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance-other/load-plan.md"
    )]
    public void M2_reader_rejects_other_archives(string profile)
    {
        using var fixture = new ManifestFixture();
        Should.Throw<InvalidOperationException>(() =>
            HotelsSourceManifest.Verify(fixture.Root, fixture.Manifest(), [profile])
        );
    }

    private sealed class ManifestFixture : IDisposable
    {
        public const string Profile =
            "openspec/changes/hotels-m2-memory-load-acceptance/load-plan.md";
        public const string Output =
            "TestResults/hotels-m2-memory-load-acceptance/run-a/source-manifest.json";
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "hotels-reader-test-" + Guid.NewGuid().ToString("N"));
        private string? redirectedLink;

        public ManifestFixture(string profile = Profile)
        {
            Directory.CreateDirectory(Root);
            Console.WriteLine("Owned manifest fixture: " + Root);
            Run("git", "init", "--quiet");
            File.WriteAllText(Path.Combine(Root, ".gitignore"), "TestResults/\n");
            File.WriteAllText(Path.Combine(Root, "source.cs"), "// fictional source\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, profile))!);
            File.WriteAllText(Path.Combine(Root, profile), "# fixed fictional profile\n");
            Directory.CreateDirectory(Path.Combine(Root, "tools/demo"));
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (
                repository is not null
                && !File.Exists(Path.Combine(repository.FullName, "Travel.slnx"))
            )
                repository = repository.Parent;
            repository.ShouldNotBeNull();
            File.Copy(
                Path.Combine(repository.FullName, "tools/demo/hotels-source-manifest.mjs"),
                Path.Combine(Root, "tools/demo/hotels-source-manifest.mjs")
            );
            Run(
                "node",
                "tools/demo/hotels-source-manifest.mjs",
                "--m2",
                "--profile",
                profile,
                "--output",
                Output
            );
        }

        public Dictionary<string, string> Manifest() =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(Root, Output))
            )!;

        public string RedirectManifestToM1()
        {
            var historical = Path.Combine(Root, "TestResults/hotels-m1");
            Directory.CreateDirectory(historical);
            File.Copy(Path.Combine(Root, Output), Path.Combine(historical, "redirected.json"));
            var alias = Path.Combine(
                Root,
                "TestResults/hotels-m2-memory-load-acceptance/redirected"
            );
            Run(
                "node",
                "-e",
                "require('node:fs').symlinkSync(process.argv[1], process.argv[2], 'junction')",
                historical,
                alias
            );
            redirectedLink = alias;
            return Path.Combine(alias, "redirected.json");
        }

        private void Run(string executable, params string[] args)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                start.ArgumentList.Add(arg);
            using var child = Process.Start(start)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(10000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                throw new TimeoutException("Owned fixture subprocess exceeded its budget.");
            }
            child.ExitCode.ShouldBe(0, stderr.GetAwaiter().GetResult());
            _ = stdout.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Path.GetDirectoryName(Path.GetFullPath(Root))
                .ShouldBe(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())));
            Path.GetFileName(Root).ShouldStartWith("hotels-reader-test-");
            if (redirectedLink is not null)
            {
                Path.GetFullPath(redirectedLink)
                    .ShouldStartWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar);
                File.GetAttributes(redirectedLink)
                    .HasFlag(FileAttributes.ReparsePoint)
                    .ShouldBeTrue();
                // Remove the owned link while its target still exists. A recursive parent
                // walk can encounter the junction after deleting its target on Windows.
                Directory.Delete(redirectedLink, recursive: false);
            }
            Directory.Delete(Root, recursive: true);
        }
    }
}
