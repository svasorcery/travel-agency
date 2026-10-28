using System.Diagnostics;
using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Trips.Evaluation;

public sealed record TripsBriefEnvelopeBudgetProof(
    bool Verified,
    bool SharedCgroup,
    long WritableBytes
);

public static class TripsBriefEnvelopeBudget
{
    public static TripsBriefEnvelopeBudgetProof Verify()
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("LinuxEnvelopeRequired");
        var memory = long.Parse(File.ReadAllText("/sys/fs/cgroup/memory.max").Trim());
        var cpu = File.ReadAllText("/sys/fs/cgroup/cpu.max")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (
            memory <= 0
            || memory > TripsBriefEvaluationResourcePolicy.MaximumMemoryBytes
            || cpu.Length != 2
            || !long.TryParse(cpu[0], out var quota)
            || !long.TryParse(cpu[1], out var period)
            || quota <= 0
            || period <= 0
            || quota > 2 * period
            || File.ReadAllText("/sys/fs/cgroup/memory.swap.max").Trim() != "0"
        )
            throw new InvalidOperationException("EnvelopeCgroupUnbounded");
        var status = File.ReadLines("/proc/self/status")
            .Single(s => s.StartsWith("Uid:", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (status.Skip(1).Any(s => s != "999"))
            throw new InvalidOperationException("EnvelopeIdentityRejected");
        var limit = File.ReadLines("/proc/self/limits")
            .Single(s => s.StartsWith("Max file size", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (
            limit.Length < 5
            || limit[3] != TripsBriefEvaluationResourcePolicy.ArchiveBytes.ToString()
            || limit[4] != limit[3]
        )
            throw new InvalidOperationException("EnvelopeFileLimitUnproven");
        var pid = File.ReadLines("/var/lib/postgresql/data/postmaster.pid").First();
        if (!int.TryParse(pid, out var postgresPid) || postgresPid <= 1)
            throw new InvalidOperationException("OwnedLedgerMissing");
        var shared =
            File.ReadAllText("/proc/self/cgroup") == File.ReadAllText("/proc/" + pid + "/cgroup");
        if (!shared)
            throw new InvalidOperationException("WorkerLedgerCgroupMismatch");
        long total = TripsBriefEvaluationResourcePolicy.ArchiveBytes;
        for (var i = 0; i < TripsBriefEvaluationResourcePolicy.TmpfsPaths.Length; i++)
        {
            var start = new ProcessStartInfo("stat")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (
                var a in new[]
                {
                    "-f",
                    "-c",
                    "%T:%S:%b",
                    TripsBriefEvaluationResourcePolicy.TmpfsPaths[i],
                }
            )
                start.ArgumentList.Add(a);
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
                throw new InvalidOperationException("EnvelopeFilesystemProbeFailed");
            var p = text.Split(':');
            if (
                p.Length != 3
                || p[0] != "tmpfs"
                || !long.TryParse(p[1], out var block)
                || !long.TryParse(p[2], out var count)
                || block <= 0
                || count <= 0
                || count > TripsBriefEvaluationResourcePolicy.TmpfsBytes[i] / block
            )
                throw new InvalidOperationException("EnvelopeFilesystemQuotaUnproven");
            total = checked(total + block * count);
        }
        if (
            new FileInfo(TripsBriefEvaluationResourcePolicy.ArchiveMount).Length
                != TripsBriefEvaluationResourcePolicy.ArchiveBytes
            || total > 256L * 1024 * 1024
        )
            throw new InvalidOperationException("EnvelopeDataBudgetExceeded");
        // Root filesystem is read-only, and only this one bounded persistent file is writable.
        var rootMount = File.ReadLines("/proc/self/mountinfo")
            .Select(s => s.Split(' '))
            .Single(p => p[4] == "/");
        if (!rootMount[5].Split(',').Contains("ro"))
            throw new InvalidOperationException("EnvelopeRootWritable");
        var evidenceMount = File.ReadLines("/proc/self/mountinfo")
            .Select(s => s.Split(' '))
            .SingleOrDefault(p => p[4] == TripsBriefEvaluationResourcePolicy.ArchiveMount);
        if (evidenceMount is null)
            throw new InvalidOperationException("EnvelopeArchiveNotBound");
        var query = new ProcessStartInfo("psql")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            var a in new[]
            {
                "-h",
                "/var/run/postgresql",
                "-U",
                "fictional_evaluator",
                "-d",
                "fictional_evaluation",
                "-Atc",
                "show listen_addresses",
            }
        )
            query.ArgumentList.Add(a);
        using (var p = Process.Start(query)!)
        {
            var listeners = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(3000) || p.ExitCode != 0 || listeners != "127.0.0.1")
                throw new InvalidOperationException("LedgerNotLoopbackOnly");
        }
        return new(true, true, total);
    }
}
