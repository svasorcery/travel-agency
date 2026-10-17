using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Hotels.Fixture;

using var shutdown = new CancellationTokenSource(TimeSpan.FromMinutes(30));
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
var scenario = args.FirstOrDefault() ?? "serve";
if (
    scenario
    is not (
        "serve"
        or "reserved"
        or "unknown"
        or "cancelled"
        or "l1"
        or "l1-host"
        or "l1-proof"
        or "l1-diagnostic"
        or "l1-diagnostic-proof"
    )
)
    throw new ArgumentException(
        "Choose serve, reserved, unknown, cancelled or explicit l1 <manifest-path>."
    );
var git = new ProcessStartInfo("git")
{
    WorkingDirectory = AppContext.BaseDirectory,
    RedirectStandardOutput = true,
    UseShellExecute = false,
    CreateNoWindow = true,
};
git.ArgumentList.Add("rev-parse");
git.ArgumentList.Add("--show-toplevel");
using var gitProcess =
    Process.Start(git) ?? throw new InvalidOperationException("Repository root unavailable.");
var root = Path.GetFullPath(gitProcess.StandardOutput.ReadToEnd().Trim());
await gitProcess.WaitForExitAsync(shutdown.Token);
if (gitProcess.ExitCode != 0 || !File.Exists(Path.Combine(root, "Travel.slnx")))
    throw new InvalidOperationException("Physical Git repository root unavailable.");
if (
    scenario is "l1" or "l1-host" or "l1-proof" or "l1-diagnostic" or "l1-diagnostic-proof"
    && (args.Length != 2 || !File.Exists(args[1]))
)
    throw new ArgumentException("Explicit l1 requires the frozen source manifest file.");
if (scenario == "l1-host")
{
    try
    {
        await HotelsL1HostProcess.RunChildAsync(root, Path.GetFullPath(args[1]), shutdown.Token);
    }
    catch (Exception)
    {
        Environment.ExitCode = 1;
    }
    return;
}
if (scenario is "l1" or "l1-proof" or "l1-diagnostic" or "l1-diagnostic-proof")
{
    _ = Task.Run(ReadStopAsync);
    try
    {
        if (scenario is "l1-proof" or "l1-diagnostic-proof")
            await HotelsL1HostProof.RunAsync(
                root,
                Path.GetFullPath(args[1]),
                shutdown.Token,
                diagnostic: scenario == "l1-diagnostic-proof"
            );
        else
        {
            await using var host = await HotelsL1HostProcess.StartAsync(
                root,
                Path.GetFullPath(args[1]),
                shutdown.Token,
                diagnostic: scenario == "l1-diagnostic"
            );
            Console.WriteLine(JsonSerializer.Serialize(new { ownedChildReady = host.Ready }));
            await new HotelsL1Runner(host, root).RunAsync(
                Path.GetFullPath(args[1]),
                shutdown.Token
            );
        }
    }
    catch (Exception ex)
    {
        Environment.ExitCode = 1;
        Console.WriteLine(
            JsonSerializer.Serialize(new { passed = false, exceptionType = ex.GetType().Name })
        );
    }
    return;
}
await using var fixture = new HotelsHostFixture();
fixture.Supplier.ChangeTermsOnRefresh = true;
fixture.Supplier.NextCreate =
    scenario == "reserved" ? "reserved"
    : scenario == "unknown" ? "lost"
    : "confirmed";
var assetReceipt = Path.GetFullPath(
    Path.Combine(
        root,
        "TestResults",
        "hotels-m1",
        $"fixture-assets-{Environment.ProcessId}-{Guid.NewGuid():N}.json"
    )
);
fixture.AssetsReady = assets =>
{
    Directory.CreateDirectory(Path.GetDirectoryName(assetReceipt)!);
    File.WriteAllText(
        assetReceipt,
        JsonSerializer.Serialize(assets, new JsonSerializerOptions { WriteIndented = true })
    );
    Console.WriteLine(JsonSerializer.Serialize(new { ownedAssets = assets, assetReceipt }));
};
await fixture.StartAsync(shutdown.Token, 5111);
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            ready = true,
            url = "http://127.0.0.1:5111",
            pid = Environment.ProcessId,
            fictional = true,
            testingMemoryOnlyConfiguration = fixture.IsolatedConfiguration,
            scenario,
        }
    )
);
_ = Task.Run(ReadStopAsync);

try
{
    if (scenario != "serve")
        _ = ObserveFictionalOutcomeAsync();
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    if (scenario == "l1")
        Environment.ExitCode = 1;
}

async Task ObserveFictionalOutcomeAsync()
{
    try
    {
        await fixture.Supplier.CreateStarted.Task.WaitAsync(shutdown.Token);
        await Task.Delay(TimeSpan.FromSeconds(5), shutdown.Token);
        var body = HotelsHostTests.Event(
            "evt_demo_" + Guid.NewGuid().ToString("N"),
            scenario == "cancelled" ? "stays.booking.cancelled" : "stays.booking.created",
            scenario == "cancelled"
                ? new JsonObject { ["id"] = fixture.Supplier.LastBookingId }
                : new JsonObject
                {
                    ["quote_id"] = fixture.Supplier.LastQuoteId,
                    ["booking_id"] = fixture.Supplier.LastBookingId,
                }
        );
        using var response = await HotelsHostTests.SendWebhook(
            fixture.Client,
            body,
            shutdown.Token
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new { fictionalObservation = scenario, status = (int)response.StatusCode }
            )
        );
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
}

async Task ReadStopAsync()
{
    try
    {
        while (await Console.In.ReadLineAsync(shutdown.Token) is { } line)
            if (line == "stop")
            {
                await shutdown.CancelAsync();
                return;
            }
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
}
