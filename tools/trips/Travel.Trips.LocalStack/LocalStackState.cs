using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Travel.Trips.LocalStack;

public sealed class LocalStackRefusal : Exception
{
    public LocalStackRefusal() { }

    public LocalStackRefusal(string message)
        : base(message) { }

    public LocalStackRefusal(string message, Exception innerException)
        : base(message, innerException) { }
}

public static class LocalStackJson
{
    public static JsonSerializerOptions Options { get; } =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
}

public sealed record LocalStackState(
    int SchemaVersion,
    string Kind,
    string Id,
    string Mode,
    string RepoRoot,
    string ProjectName,
    int WebPort,
    int OidcPort,
    string? ApplicationAsOfUtc,
    DateTimeOffset CreatedAtUtc,
    string DockerEndpoint,
    string DockerId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? E2eProfile = null
)
{
    [JsonIgnore]
    public string StateDir { get; private init; } = "";

    public static LocalStackState ReadLocal(string stateDir, string repoRoot)
    {
        if (!Path.IsPathFullyQualified(stateDir) || !Path.IsPathFullyQualified(repoRoot))
            throw new LocalStackRefusal("Absolute owned paths are required.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDir));
        var state = Read(directory);
        var expected = Path.Combine(
            root,
            "TestResults",
            "trips-local-stack",
            state.Mode == "demo" ? "demo" : "e2e-" + state.Id
        );
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (
            !string.Equals(directory, expected, comparison)
            || !string.Equals(root, state.RepoRoot, comparison)
        )
            throw new LocalStackRefusal("State does not belong to this checkout.");
        return state;
    }

    public static LocalStackState ReadContainer(string stateDir)
    {
        if (
            !OperatingSystem.IsLinux()
            || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true"
            || stateDir != "/state"
        )
            throw new LocalStackRefusal(
                "This command requires the owned container and /state mount."
            );
        return Read(stateDir);
    }

    private static LocalStackState Read(string directory)
    {
        var file = Path.Combine(directory, "state.json");
        RequireNoLinks(file);
        if (new FileInfo(file).Length is <= 0 or > 16384)
            throw new LocalStackRefusal("Invalid state size.");
        var bytes = File.ReadAllBytes(file);
        using var document = JsonDocument.Parse(bytes);
        if (
            document.RootElement.ValueKind != JsonValueKind.Object
            || document
                .RootElement.EnumerateObject()
                .Select(p => p.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != document.RootElement.EnumerateObject().Count()
        )
            throw new LocalStackRefusal("Invalid state fields.");
        var state =
            JsonSerializer.Deserialize<LocalStackState>(bytes, LocalStackJson.Options)
            ?? throw new LocalStackRefusal("Missing state.");
        var e2e = state.Mode == "e2e";
        if (
            state.SchemaVersion != 1
            || state.Kind != "travel-trips-local-stack"
            || state.Id is null
            || !Regex.IsMatch(state.Id, "^[a-f0-9]{32}$")
            || state.Mode is not ("demo" or "e2e")
            || state.ProjectName != $"travel-trips-{state.Mode}-{state.Id}"
            || state.WebPort != (e2e ? 4206 : 4205)
            || state.OidcPort != (e2e ? 8186 : 8185)
            || (!e2e && state.E2eProfile is not null)
            || state.ApplicationAsOfUtc
                != (e2e ? FixedE2eTimeProvider.ProfileInstant(state.E2eProfile ?? "moscow") : null)
            || state.CreatedAtUtc == default
            || state.CreatedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(state.RepoRoot)
            || state.RepoRoot.Any(char.IsControl)
            || !(
                state.RepoRoot.StartsWith('/') || Regex.IsMatch(state.RepoRoot, "^[A-Za-z]:[\\\\/]")
            )
            || string.IsNullOrWhiteSpace(state.DockerId)
            || state.DockerId.Length > 256
            || state.DockerId.Any(char.IsControl)
            || !IsLocalEndpoint(state.DockerEndpoint)
        )
            throw new LocalStackRefusal("Invalid owned state profile.");
        return state with { StateDir = directory };
    }

    private static bool IsLocalEndpoint(string? endpoint) =>
        endpoint is not null
        && (
            Regex.IsMatch(endpoint, "^unix:///[A-Za-z0-9_./-]+$")
            || Regex.IsMatch(endpoint, "^npipe:/{2,4}\\./pipe/[A-Za-z0-9_.-]+$")
        );

    public static void RequireNoLinks(string path)
    {
        for (
            var current = Path.GetFullPath(path);
            current is not null;
            current = Path.GetDirectoryName(current)
        )
            if (
                (File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0
            )
                throw new LocalStackRefusal("Linked state paths are not supported.");
    }
}
