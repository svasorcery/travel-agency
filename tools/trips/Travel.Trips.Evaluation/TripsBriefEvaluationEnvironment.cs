using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travel.AI.TripsBriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Travel.AI.Tests")]

namespace Travel.Trips.Evaluation;

public sealed class TripsBriefEvaluationEnvironment : ITripsBriefEvaluationEnvironment
{
    public TimeProvider Clock => TimeProvider.System;

    public TripsBriefEvaluationArtifacts InspectArtifacts(string repoRoot)
    {
        var root = Path.GetFullPath(repoRoot);
        if (
            Encoding
                .UTF8.GetString(Run("git", root, "rev-parse", "--show-toplevel"))
                .Trim()
                .Replace('\\', '/') != root.Replace('\\', '/')
        )
            throw new InvalidOperationException("RepositoryRootRejected");
        string Source(string relative)
        {
            var path = Path.Combine(root, relative);
            AssertPhysicalFile(path, root);
            return Hash(File.ReadAllBytes(path));
        }
        var closure = InspectBuildClosure(AppContext.BaseDirectory);
        var packages = new[]
        {
            "Directory.Build.props",
            "Directory.Packages.props",
            "global.json",
            "apps/Travel.AI/Travel.AI.csproj",
            "modules/trips/Travel.Modules.Trips.Application/Travel.Modules.Trips.Application.csproj",
            "tools/trips/Travel.Trips.Evaluation/Travel.Trips.Evaluation.csproj",
        };
        var sourceClean =
            Run("git", root, "status", "--porcelain", "--untracked-files=normal").Length == 0;
        TripsBriefEnvelopePreparation? prepared = File.Exists(
            TripsBriefEvaluationEnvelope.PreparationPath(root)
        )
            ? JsonSerializer.Deserialize<TripsBriefEnvelopePreparation>(
                File.ReadAllText(TripsBriefEvaluationEnvelope.PreparationPath(root))
            )
            : null;
        return new(
            Encoding.UTF8.GetString(Run("git", root, "rev-parse", "HEAD")).Trim(),
            Hash(Run("git", root, "ls-tree", "-rz", "--full-tree", "HEAD")),
            Source(TripsBriefEvaluationCommand.PacketRelativePath),
            TripsBriefEvaluationRunner.EmbeddedPromptSha256(),
            TripsBriefExtractor.SchemaSha256(),
            Hash(
                Encoding.UTF8.GetBytes(
                    Source(
                        "modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefCandidateValidator.cs"
                    )
                        + Source(
                            "modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefDateNormalizer.cs"
                        )
                )
            ),
            Source(
                "modules/trips/Travel.Modules.Trips.Application/BriefExtraction/TripBriefLexicon.cs"
            ),
            Hash(
                Encoding.UTF8.GetBytes(
                    string.Join('\n', packages.Select(p => p + "\0" + Source(p)))
                )
            ),
            closure.Hash,
            sourceClean && closure.IsClean,
            TripsBriefWireProjection.Sha256(),
            TripsBriefEvaluationResourcePolicy.Sha256(),
            prepared is null
                ? ""
                : TripsBriefEvaluationEnvelope.Closure(
                    TripsBriefEvaluationEnvelope.PublishPath(root)
                ),
            prepared?.ImageId ?? ""
        );
    }

    internal static (string Hash, bool IsClean) InspectBuildClosure(string root)
    {
        var files = Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length > 4096)
            throw new InvalidOperationException("BuildClosureTooLarge");
        var identityFiles = files
            .Where(p =>
                Path.GetFileName(p) is "Travel.Trips.Evaluation" or "Travel.AI"
                || p.EndsWith(".dll", StringComparison.Ordinal)
                || p.EndsWith(".exe", StringComparison.Ordinal)
                || p.EndsWith(".so", StringComparison.Ordinal)
                || p.EndsWith(".dylib", StringComparison.Ordinal)
                || p.EndsWith(".deps.json", StringComparison.Ordinal)
                || p.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)
                || Path.GetRelativePath(root, p) == "Travel.AI.staticwebassets.endpoints.json"
            )
            .ToArray();
        var build = new StringBuilder();
        foreach (var file in identityFiles)
        {
            AssertPhysicalFile(file, root);
            build
                .Append(Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Append('\0')
                .Append(Hash(File.ReadAllBytes(file)))
                .Append('\n');
        }
        var allowed = files.All(p =>
            identityFiles.Contains(p)
            || p.EndsWith(".pdb", StringComparison.Ordinal)
            || p.EndsWith(".xml", StringComparison.Ordinal)
            || Path.GetFileName(p).StartsWith("appsettings", StringComparison.Ordinal)
        );
        return (
            Hash(Encoding.UTF8.GetBytes(build.ToString())),
            allowed && identityFiles.Length > 0
        );
    }

    private static void AssertPhysicalFile(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var boundary =
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (
            !full.StartsWith(boundary, StringComparison.Ordinal)
            || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0
        )
            throw new InvalidOperationException("ArtifactPathRejected");
        for (
            var d = new FileInfo(full).Directory;
            d is not null && d.FullName.Length > boundary.Length;
            d = d.Parent
        )
            if ((d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("ArtifactLinkRejected");
    }

    public Task<TripsBriefEnvelopeOutcome> RunOwnedAsync(
        TripsBriefEvaluationManifest manifest,
        string root,
        string hash,
        TripsBriefEvaluationAuthorizationClock authority,
        CancellationToken ct
    )
    {
        if (
            !TripsBriefEvaluationAuthorization
                .Check(manifest, InspectArtifacts(root), authority.EffectiveUtcNow)
                .CanConstructExecutionResources
        )
            throw new InvalidOperationException("BoundWorkerAdmissionRejected");
        return TripsBriefEvaluationEnvelope.ExecuteAsync(
            root,
            manifest,
            hash,
            authority,
            () =>
                TripsBriefEvaluationAuthorization
                    .Check(manifest, InspectArtifacts(root), authority.EffectiveUtcNow)
                    .CanConstructExecutionResources,
            ct
        );
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static byte[] Run(string executable, string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments)
            start.ArgumentList.Add(a);
        using var p =
            Process.Start(start) ?? throw new InvalidOperationException("InspectionUnavailable");
        var errors = p.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(bytes);
        if (!p.WaitForExit(15000))
        {
            p.Kill(true);
            throw new InvalidOperationException("InspectionDeadline");
        }
        copy.GetAwaiter().GetResult();
        errors.GetAwaiter().GetResult();
        if (p.ExitCode != 0 || bytes.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("InspectionFailed");
        return bytes.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
