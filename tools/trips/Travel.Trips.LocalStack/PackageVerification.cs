using System.Globalization;
using System.Text.RegularExpressions;
using Travel.Modules.Trips.Infrastructure.Itineraries;

namespace Travel.Trips.LocalStack;

public static class PackageVerification
{
    public static async Task<PackageCheckResult> CheckAsync(
        string directory,
        string version,
        string manifestSha,
        string asOf,
        CancellationToken ct
    )
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new LocalStackRefusal("An existing absolute package directory is required.");
        for (
            var ancestor = new DirectoryInfo(directory);
            ancestor is not null;
            ancestor = ancestor.Parent
        )
            if (ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new LocalStackRefusal("Package paths cannot cross a reparse point.");
        if (
            asOf.Length > 40
            || !Regex.IsMatch(
                asOf,
                @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$"
            )
            || !DateTimeOffset.TryParse(
                asOf,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var at
            )
            || at.Offset != TimeSpan.Zero
        )
            throw new LocalStackRefusal("An explicit UTC evaluation instant is required.");
        CuratedMoscowRelease release;
        try
        {
            release = new CuratedMoscowRelease("moscow-outdoor", version, manifestSha);
        }
        catch (ArgumentException)
        {
            throw new LocalStackRefusal("Invalid candidate release identity.");
        }
        var result = await new CuratedMoscowFacts(
            directory,
            new EvaluationClock(at),
            expectedRelease: release
        ).ReadAsync(ct);
        if (result.IsError)
            throw new LocalStackRefusal("Package integrity, semantics or validity check failed.");
        var facts = result.Value;
        return new(
            true,
            "validated-not-released",
            release.DatasetId,
            release.Version,
            release.ManifestSha256,
            at,
            facts.ReleaseValidUntil,
            facts.Pois.Count,
            facts.Edges.Count
        );
    }

    private sealed class EvaluationClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }
}

public sealed record PackageCheckResult(
    bool Ok,
    string Status,
    string DatasetId,
    string Version,
    string ManifestSha256,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset ReleaseValidUntil,
    int Pois,
    int Edges
);
