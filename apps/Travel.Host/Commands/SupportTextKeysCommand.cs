using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel.Modules.Support.Api.Composition;

namespace Travel.Host.Commands;

public static class SupportTextKeysCommand
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
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { Args = [] }
        );
        builder.Logging.ClearProviders();
        if (configuration is not null)
            builder.Configuration.AddConfiguration(configuration);
        var result = SupportTextKeys.Initialize(builder.Configuration, TimeProvider.System);
        await output.WriteLineAsync(result.Code);
        return result.ExitCode;
    }
}
