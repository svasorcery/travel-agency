using System.Security.Cryptography;
using System.Text;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public static class TripsBriefEvaluationResourcePolicy
{
    public const string Version = "trips-owned-envelope-v1";
    public const long MaximumMemoryBytes = 2L * 1024 * 1024 * 1024;
    public const long NanoCpus = 2_000_000_000;
    public const long ArchiveBytes = 64L * 1024 * 1024;
    public const int LifetimeSeconds = 6 * 60 * 60;
    public const string ArchiveMount = "/evidence/current.journal";
    public static readonly string[] TmpfsPaths =
    [
        "/var/lib/postgresql/data",
        "/var/run/postgresql",
        "/tmp",
        "/evaluation",
        "/dev/shm",
    ];
    public static readonly long[] TmpfsBytes =
    [
        112L * 1024 * 1024,
        4L * 1024 * 1024,
        12L * 1024 * 1024,
        32L * 1024 * 1024,
        32L * 1024 * 1024,
    ];

    public static string Sha256() =>
        Convert.ToHexStringLower(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    Version
                        + ":2000000000:2147483648:67108864:21600:112,4,12,32,32:readonly:uid999:drop-all:fsize64:single-bind"
                )
            )
        );
}
