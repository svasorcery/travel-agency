using Travel.Host.Tests.Integration.Rail;

// Parsing/describe/build cannot construct a fixture or start resources.
if (args.Length == 0 || args[0] == "--describe")
{
    Console.WriteLine(
        "Rail actual-Host fictional profile; separate actual Host/DB and measured authorization required. See tools/rail/measured-profile.json. No resources started."
    );
    return;
}
if (args.Length != 4 || args[0] != "--run-authorized" || args[1] != "--actual-host-db-authorized")
    throw new ArgumentException(
        "Both explicit manual invocation guards, profile path and new receipt directory are required."
    );
using var shutdown = new CancellationTokenSource(TimeSpan.FromMinutes(45));
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Environment.ExitCode = await RailMeasuredProfile.RunAsync(
    Path.GetFullPath(args[2]),
    Path.GetFullPath(args[3]),
    shutdown.Token
);
