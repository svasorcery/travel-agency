using Travel.Modules.Flights.Api.Composition;

namespace Travel.Host.Commands;

public static class FlightsPiiKeysCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        IConfiguration? configuration = null
    )
    {
        if (!args.SequenceEqual(new[] { "initialize", "--execute" }))
        {
            await output.WriteLineAsync(
                "InvalidArguments: use initialize --execute with explicit protection configuration."
            );
            return 2;
        }
        // Key-only composition. Do not build/start the web Host or register any stores.
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { Args = [] }
        );
        builder.Logging.ClearProviders();
        if (configuration is not null)
            builder.Configuration.AddConfiguration(configuration);
        var result = FlightsPiiKeys.Initialize(builder.Configuration, TimeProvider.System);
        await output.WriteLineAsync(result.Code);
        return result.ExitCode;
    }
}
