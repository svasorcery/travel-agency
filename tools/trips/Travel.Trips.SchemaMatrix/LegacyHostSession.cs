using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Travel.Trips.SchemaMatrix;

// The earlier Host runs in its own process and load context. Its temporary DB and
// ring settings travel only through the private stdin pipe of that child process.
internal sealed class LegacyHostSession(Process child) : IAsyncDisposable
{
    private const string Prefix = "MATRIX-RESULT ";

    internal static async Task<LegacyHostSession> Start(
        string root,
        FixtureConfig config,
        CancellationToken ct
    )
    {
        var assembly = Path.Combine(
            root,
            "tools",
            "trips",
            "Travel.Trips.SchemaMatrix",
            "bin",
            "Debug",
            "net10.0",
            "Travel.Trips.SchemaMatrix.dll"
        );
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add(assembly);
        info.ArgumentList.Add("legacy");
        var child =
            Process.Start(info)
            ?? throw new InvalidOperationException("Legacy Host could not start.");
        var session = new LegacyHostSession(child);
        try
        {
            await session.Command(JsonSerializer.Serialize(config), ct);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    internal async Task<string> Command(string command, CancellationToken ct)
    {
        await child.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        await child.StandardInput.FlushAsync(ct);
        while (await child.StandardOutput.ReadLineAsync(ct) is { } line)
            if (line.StartsWith(Prefix, StringComparison.Ordinal))
                return line[Prefix.Length..];
        throw new InvalidOperationException("Legacy Host ended before replying.");
    }

    internal async Task<Guid> Create(CancellationToken ct) =>
        Guid.Parse(await Command("create", ct));

    internal async Task Open(Guid id, CancellationToken ct)
    {
        if (await Command(id.ToString("D"), ct) != "ok")
            throw new InvalidOperationException("Legacy read failed.");
    }

    internal static async Task Serve(CancellationToken ct)
    {
        var message =
            await Console.In.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("Missing fixture.");
        var config =
            JsonSerializer.Deserialize<FixtureConfig>(message)
            ?? throw new InvalidOperationException("Missing configuration.");
        await using var host = new MatrixHost(config);
        using var client = host.CreateClient();
        await using var db = ToolProgram.Db(config);
        if (db.Database.GetMigrations().Last() != ToolProgram.Previous)
            throw new InvalidOperationException("Expected the preceding Host migration assembly.");
        Console.WriteLine(Prefix + "ready");
        while (await Console.In.ReadLineAsync(ct) is { } command)
        {
            if (command == "create")
                Console.WriteLine(
                    Prefix + (await MatrixHost.CreateManual(client, ct)).ToString("D")
                );
            else
            {
                await MatrixHost.OpenManual(client, Guid.Parse(command), ct);
                Console.WriteLine(Prefix + "ok");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            child.StandardInput.Close();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await child.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        finally
        {
            child.Dispose();
        }
    }
}
