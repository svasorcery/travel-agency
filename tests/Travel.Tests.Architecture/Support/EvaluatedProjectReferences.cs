using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Travel.Tests.Architecture.Support;

internal static class EvaluatedProjectReferences
{
    private const int MaxMsBuildOutputCharacters = 2 * 1024 * 1024;
    private static readonly TimeSpan MsBuildEvaluationTimeout = TimeSpan.FromSeconds(20);
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly SemaphoreSlim MsBuildProcessSlots = new(
        Math.Clamp(Environment.ProcessorCount / 4, 1, 2)
    );
    private static readonly ConcurrentDictionary<
        EvaluationKey,
        Lazy<Task<string[]>>
    > EvaluationCache = new();
    private static readonly ConcurrentDictionary<
        ItemEvaluationKey,
        Lazy<Task<string[]>>
    > ItemEvaluationCache = new();
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static Task<string[]> ForProjectAsync(string projectPath, string configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        var key = new EvaluationKey(Path.GetFullPath(projectPath), configuration);
        return EvaluationCache
            .GetOrAdd(
                key,
                static key => new Lazy<Task<string[]>>(
                    () => RunMsBuildEvaluationAsync(key),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }

    public static async Task<string[]> ForProjectClosureAsync(
        string projectPath,
        string configuration
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);

        var root = Path.GetFullPath(projectPath);
        var visited = new HashSet<string>(PathComparer) { root };
        var closure = new HashSet<string>(PathComparer);
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.TryPop(out var current))
        {
            var references = await ForProjectAsync(current, configuration);
            foreach (var reference in references)
            {
                closure.Add(reference);
                if (visited.Add(reference))
                    pending.Push(reference);
            }
        }

        return closure.OrderBy(path => path, PathComparer).ToArray();
    }

    public static Task<string[]> ForItemIdentitiesAsync(
        string projectPath,
        string configuration,
        string itemName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        if (!itemName.All(character => char.IsLetterOrDigit(character) || character == '_'))
            throw new ArgumentException("MSBuild item name is invalid.", nameof(itemName));

        var key = new ItemEvaluationKey(Path.GetFullPath(projectPath), configuration, itemName);
        return ItemEvaluationCache
            .GetOrAdd(
                key,
                static key => new Lazy<Task<string[]>>(
                    () => RunMsBuildItemIdentityEvaluationAsync(key),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;
    }

    private static async Task<string[]> RunMsBuildEvaluationAsync(EvaluationKey key)
    {
        if (!File.Exists(key.ProjectPath))
        {
            throw new InvalidOperationException(
                $"MSBuild evaluation project does not exist: {key.ProjectPath}"
            );
        }

        var startInfo = CreateMsBuildStartInfo(key);
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        var result = await RunProcessAsync(
            startInfo,
            $"MSBuild project-reference evaluation for {key.ProjectPath} ({key.Configuration})"
        );
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                $"MSBuild project-reference evaluation returned empty output for {key.ProjectPath} ({key.Configuration})."
            );
        }

        return ParseProjectReferences(result.StandardOutput, key);
    }

    private static async Task<string[]> RunMsBuildItemIdentityEvaluationAsync(ItemEvaluationKey key)
    {
        if (!File.Exists(key.ProjectPath))
            throw new InvalidOperationException(
                $"MSBuild evaluation project does not exist: {key.ProjectPath}"
            );

        var startInfo = CreateMsBuildStartInfo(
            new EvaluationKey(key.ProjectPath, key.Configuration)
        );
        startInfo.ArgumentList.Add($"-getItem:{key.ItemName}");
        var context =
            $"MSBuild {key.ItemName} evaluation for {key.ProjectPath} ({key.Configuration})";
        var result = await RunProcessAsync(startInfo, context);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException($"{context} returned empty output.");

        return ParseItemIdentities(result.StandardOutput, key);
    }

    private static ProcessStartInfo CreateMsBuildStartInfo(EvaluationKey key)
    {
        var dotnetHostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(dotnetHostPath) ? "dotnet" : dotnetHostPath,
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(key.ProjectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.ArgumentList.Add("-maxcpucount:1");
        startInfo.ArgumentList.Add($"-property:Configuration={key.Configuration}");
        return startInfo;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        string context
    )
    {
        await MsBuildProcessSlots.WaitAsync();
        try
        {
            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("the process API returned false");
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Could not launch {context}.", exception);
            }

            var standardOutput = ReadBoundedOutputAsync(
                process.StandardOutput,
                "standard output",
                MaxMsBuildOutputCharacters
            );
            var standardError = ReadBoundedOutputAsync(
                process.StandardError,
                "standard error",
                MaxMsBuildOutputCharacters
            );
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(), standardOutput, standardError)
                    .WaitAsync(MsBuildEvaluationTimeout);
            }
            catch (TimeoutException exception)
            {
                TryKill(process);
                var partialOutput = await DrainAfterKillAsync(standardOutput, standardError);
                throw new InvalidOperationException(
                    $"{context} timed out after {MsBuildEvaluationTimeout.TotalSeconds:0.###} seconds. stdout: {Abbreviate(partialOutput.StandardOutput)}. stderr: {Abbreviate(partialOutput.StandardError)}",
                    exception
                );
            }
            catch (Exception exception)
            {
                TryKill(process);
                await DrainAfterKillAsync(standardOutput, standardError);
                throw new InvalidOperationException($"{context} output failed.", exception);
            }

            var output = await standardOutput;
            var error = await standardError;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{context} exited with code {process.ExitCode}. stdout: {Abbreviate(output)}. stderr: {Abbreviate(error)}"
                );
            }

            return new ProcessResult(output, error);
        }
        finally
        {
            MsBuildProcessSlots.Release();
        }
    }

    private static async Task<string> ReadBoundedOutputAsync(
        StreamReader reader,
        string streamName,
        int maxCharacters
    )
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
                return output.ToString();

            if (output.Length + read > maxCharacters)
            {
                throw new InvalidOperationException(
                    $"MSBuild {streamName} exceeded {maxCharacters} characters."
                );
            }

            output.Append(buffer, 0, read);
        }
    }

    private static string[] ParseProjectReferences(string output, EvaluationKey key)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Object
                || !itemsElement.TryGetProperty("ProjectReference", out var referencesElement)
                || referencesElement.ValueKind != JsonValueKind.Array
            )
            {
                throw new InvalidOperationException(
                    "JSON does not contain a ProjectReference Items array."
                );
            }

            return referencesElement
                .EnumerateArray()
                .Select(ParseProjectReference)
                .Distinct(PathComparer)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild project-reference evaluation returned invalid JSON for {key.ProjectPath} ({key.Configuration}).",
                exception
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild project-reference evaluation returned incomplete JSON for {key.ProjectPath} ({key.Configuration}): {exception.Message}",
                exception
            );
        }
    }

    internal static string[] ParseItemIdentitiesForTesting(
        string output,
        string projectPath,
        string configuration,
        string itemName
    ) => ParseItemIdentities(output, new ItemEvaluationKey(projectPath, configuration, itemName));

    private static string[] ParseItemIdentities(string output, ItemEvaluationKey key)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Object
                || !itemsElement.TryGetProperty(key.ItemName, out var itemArray)
                || itemArray.ValueKind != JsonValueKind.Array
            )
                throw new InvalidOperationException(
                    $"JSON does not contain a {key.ItemName} Items array."
                );

            return itemArray
                .EnumerateArray()
                .Select(item => ParseItemIdentity(item, key.ItemName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild {key.ItemName} evaluation returned invalid JSON for {key.ProjectPath} ({key.Configuration}).",
                exception
            );
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"MSBuild {key.ItemName} evaluation returned incomplete JSON for {key.ProjectPath} ({key.Configuration}): {exception.Message}",
                exception
            );
        }
    }

    private static string ParseItemIdentity(JsonElement item, string itemName)
    {
        if (
            item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("Identity", out var identityElement)
            || identityElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(identityElement.GetString())
        )
            throw new InvalidOperationException(
                $"{itemName} item is missing a non-empty Identity metadata value."
            );

        return identityElement.GetString()!;
    }

    private static string ParseProjectReference(JsonElement item)
    {
        if (
            item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("FullPath", out var fullPathElement)
            || fullPathElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(fullPathElement.GetString())
        )
        {
            throw new InvalidOperationException(
                "ProjectReference item is missing a non-empty FullPath metadata value."
            );
        }

        var fullPath = fullPathElement.GetString()!;
        if (!Path.IsPathFullyQualified(fullPath))
            throw new InvalidOperationException(
                $"ProjectReference FullPath is not absolute: {fullPath}"
            );

        return Path.GetFullPath(fullPath);
    }

    private static async Task<ProcessResult> DrainAfterKillAsync(
        Task<string> standardOutput,
        Task<string> standardError
    )
    {
        try
        {
            await Task.WhenAll(standardOutput, standardError).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Preserve the launch, timeout, or output-bound failure that triggered cleanup.
        }

        return new ProcessResult(
            standardOutput.IsCompletedSuccessfully ? await standardOutput : string.Empty,
            standardError.IsCompletedSuccessfully ? await standardError : string.Empty
        );
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cleanup is best-effort; the evaluation still fails closed.
        }
    }

    private static string Abbreviate(string value)
    {
        const int maxCharacters = 2000;
        const string marker = "...<truncated>...";
        if (value.Length <= maxCharacters)
            return value;

        var edgeCharacters = (maxCharacters - marker.Length) / 2;
        return $"{value[..edgeCharacters]}{marker}{value[^edgeCharacters..]}";
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing Travel.slnx."
        );
    }

    private readonly record struct EvaluationKey(string ProjectPath, string Configuration);

    private readonly record struct ItemEvaluationKey(
        string ProjectPath,
        string Configuration,
        string ItemName
    );

    private readonly record struct ProcessResult(string StandardOutput, string StandardError);
}
