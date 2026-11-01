using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal static class HotelsM2Docker
{
    internal static string RunTag(HotelM2LoadRunSpec spec) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(spec.Output)))[..24]
            .ToLowerInvariant();

    internal static Task<string> ImageIdAsync(HotelM2LoadRunSpec spec, CancellationToken ct) =>
        ReadAsync(["image", "inspect", spec.DatabaseImage, "--format", "{{.Id}}"], ct);

    internal static async Task<object> OwnedStatsAsync(
        HotelsL1HostProcess host,
        CancellationToken ct
    )
    {
        var spec = host.LoadSpec!;
        var id = host.Ready.GetProperty("assets").GetProperty("containerId").GetString()!;
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException("Owned container identity missing.");
        var identity = await ReadAsync(
            [
                "inspect",
                "--format",
                "{{index .Config.Labels \"travel.hotels.load.owner-pid\"}}|{{index .Config.Labels \"travel.hotels.load.run-id\"}}|{{.Image}}",
                id,
            ],
            ct
        );
        var expected =
            host.Process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "|"
            + RunTag(spec)
            + "|"
            + spec.DatabaseImageId;
        if (identity != expected)
            throw new InvalidOperationException("Container labels/image do not match this child.");
        var stats = await ReadAsync(
            ["stats", "--no-stream", "--format", "{{.CPUPerc}}|{{.MemUsage}}|{{.BlockIO}}", id],
            ct
        );
        var fields = stats.Split('|');
        if (fields.Length != 3)
            throw new InvalidOperationException("Owned DB metrics unavailable.");
        var cgroup = await ReadAsync(
            ["exec", id, "cat", "/sys/fs/cgroup/cpu.stat", "/sys/fs/cgroup/memory.current"],
            ct
        );
        var cpu = System.Text.RegularExpressions.Regex.Match(
            cgroup,
            @"(?m)^usage_usec\s+(\d+)\s*$"
        );
        var memoryLine = cgroup
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (
            !cpu.Success
            || !ulong.TryParse(cpu.Groups[1].Value, out var cpuMicroseconds)
            || !ulong.TryParse(memoryLine, out var memoryBytes)
        )
            throw new InvalidOperationException("Owned cgroup-v2 counters unavailable.");
        var storage = await ReadAsync(["exec", id, "du", "-sb", "/var/lib/postgresql"], ct);
        if (
            !long.TryParse(
                storage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0],
                out var databaseDirectoryApparentBytes
            )
        )
            throw new InvalidOperationException("Owned DB storage unavailable.");
        var temp = Path.GetFullPath(
            host.Ready.GetProperty("assets").GetProperty("temporaryDirectory").GetString()!
        );
        if (
            !temp.StartsWith(
                Path.GetFullPath(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase
            )
            || !Path.GetFileName(temp).StartsWith("travel-hotels-host-", StringComparison.Ordinal)
            || (File.GetAttributes(temp) & FileAttributes.ReparsePoint) != 0
        )
            throw new InvalidOperationException("Owned temp storage identity mismatch.");
        long ownTemporaryBytes = 0;
        foreach (
            var file in new DirectoryInfo(temp).EnumerateFiles("*", SearchOption.AllDirectories)
        )
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Redirected temporary file.");
            ownTemporaryBytes = checked(ownTemporaryBytes + file.Length);
        }
        return new
        {
            databaseDirectoryApparentBytes,
            ownTemporaryBytes,
            storageNote = "Apparent bytes in owned PostgreSQL data tree and own local temp files; not host-wide disk use.",
            containerId = id,
            identityVerified = true,
            cpuMicroseconds,
            memoryBytes,
            cpuPercent = fields[0],
            memoryUsage = fields[1],
            blockIo = fields[2],
            note = "Docker's formatted container metrics; not added to VM or Host RSS.",
        };
    }

    internal static async Task<string> ReadAsync(string[] arguments, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var start = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Docker metadata reader unavailable.");
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var text = await output;
            if (process.ExitCode != 0 || text.Length > 16384)
                throw new InvalidOperationException("Owned Docker metadata unavailable.");
            await error;
            return text.Trim();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}

internal static class HotelsM2LoadCampaign
{
    internal static async Task RunAsync(
        string root,
        string smallPath,
        string largePath,
        bool execute,
        CancellationToken ct
    )
    {
        var small = HotelM2LoadRunSpec.Read(root, smallPath);
        var large = HotelM2LoadRunSpec.Read(root, largePath);
        HotelsM2MemoryAssessment.ValidateVersions(small.Version, large.Version);
        if (
            small.Code != "S"
            || large.Code != "L"
            || small.CheckIn != large.CheckIn
            || small.Manifest != large.Manifest
            || !small.Inputs.SequenceEqual(large.Inputs)
            || small.DatabaseImage != large.DatabaseImage
            || small.DatabaseImageId != large.DatabaseImageId
            || Path.GetDirectoryName(small.Output) != Path.GetDirectoryName(large.Output)
            || small.Output == large.Output
        )
            throw new InvalidDataException("Exactly one coherent S/L packet is required.");
        if (!execute)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        prepared = true,
                        acceptanceVersion = small.Version,
                        executed = false,
                        profiles = new[] { "S", "L" },
                        planned = 6304,
                    }
                )
            );
            return;
        }
        if (
            !OperatingSystem.IsWindows()
            || small.CheckIn <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        )
            throw new InvalidOperationException(
                "Frozen local profile environment/dates are unavailable."
            );
        var smallOutput = HotelM2LoadRunSpec.Resolve(root, small.Output, true);
        var largeOutput = HotelM2LoadRunSpec.Resolve(root, large.Output, true);
        var output = Path.GetDirectoryName(smallOutput)!;
        if (Directory.Exists(smallOutput) || Directory.Exists(largeOutput))
            throw new InvalidOperationException("Existing profile evidence must be retained.");
        Directory.CreateDirectory(output);
        await using var marker = new FileStream(
            Path.Combine(output, "campaign.json"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        var result = new Dictionary<string, object?>
        {
            ["planned"] = 6304,
            ["acceptanceVersion"] = small.Version,
            ["S"] = "not-run",
            ["L"] = "not-run",
            ["passed"] = false,
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        var start = Stopwatch.StartNew();
        try
        {
            if (await HotelsM2Docker.ImageIdAsync(small, deadline.Token) != small.DatabaseImageId)
                throw new InvalidOperationException(
                    "Pinned local PostgreSQL image mismatch; no pull is allowed."
                );
            foreach (var (spec, path) in new[] { (small, smallPath), (large, largePath) })
            {
                var destination = HotelM2LoadRunSpec.Resolve(root, spec.Output, true);
                Directory.CreateDirectory(destination);
                HotelM2ResourceGuard.Check(
                    spec,
                    destination,
                    value =>
                    {
                        using var file = new FileStream(
                            Path.Combine(destination, "pre-start-resources.json"),
                            FileMode.CreateNew
                        );
                        JsonSerializer.Serialize(file, value, HotelM2LoadRunSpec.Json);
                    }
                );
                result[spec.Code] = "running";
                using var setup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                setup.CancelAfter(TimeSpan.FromSeconds(120));
                await using var host = await HotelsL1HostProcess.StartAsync(
                    root,
                    HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false),
                    setup.Token,
                    diagnostic: true,
                    loadSpec: spec,
                    profilePath: Path.GetFullPath(path)
                );
                await new HotelsL1Runner(host, root).RunAsync(
                    HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false),
                    deadline.Token
                );
                result[spec.Code] = "passed";
            }
            result["passed"] = true;
        }
        catch (Exception ex)
        {
            foreach (var code in new[] { "S", "L" })
                if (result[code] as string == "running")
                    result[code] = "failed";
            result["exceptionType"] = ex.GetType().Name;
            throw;
        }
        finally
        {
            var accepted = await HotelsM2Completion.FinishAsync(
                _ => Task.CompletedTask,
                () => Task.CompletedTask,
                async (passed, errors) =>
                {
                    result["passed"] = passed;
                    result["finalizationErrors"] = errors;
                    result["elapsedSeconds"] = start.Elapsed.TotalSeconds;
                    result["cancelled"] = deadline.IsCancellationRequested;
                    marker.Position = 0;
                    marker.SetLength(0);
                    await JsonSerializer.SerializeAsync(
                        marker,
                        result,
                        HotelM2LoadRunSpec.Json,
                        CancellationToken.None
                    );
                    await marker.FlushAsync(CancellationToken.None);
                },
                () =>
                    result["S"] as string == "passed"
                    && result["L"] as string == "passed"
                    && !deadline.IsCancellationRequested
                    && start.Elapsed <= TimeSpan.FromMinutes(20)
            );
            if (!accepted)
                throw new InvalidOperationException("M2 campaign failed, expired or incomplete.");
        }
    }
}
