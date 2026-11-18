using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Travel.Host.Tests.Integration.Aspire;

// xUnit can report its outer timeout before the fixture has finished unwinding.
// Persist phase names, elapsed times and the existing sanitized failure summary
// before disposal. Raw resource logs and connection details never enter this file.
internal sealed class AspireSmokeProgress(StreamWriter writer) : IDisposable
{
    private readonly Stopwatch elapsed = Stopwatch.StartNew();

    internal static AspireSmokeProgress Begin([CallerFilePath] string testFile = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "../.."));
        var directory = Directory.CreateDirectory(
            Path.Combine(root, "TestResults", "aspire-smoke")
        );
        var file = Path.Combine(directory.FullName, $"fresh-stack-{Guid.NewGuid():N}.tsv");
        return new(
            new StreamWriter(
                new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read)
            )
            {
                AutoFlush = true,
            }
        );
    }

    internal string Record(string phase)
    {
        lock (writer)
            writer.WriteLine(
                $"{elapsed.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)}\t{phase}"
            );
        return phase;
    }

    public void Dispose() => writer.Dispose();
}
