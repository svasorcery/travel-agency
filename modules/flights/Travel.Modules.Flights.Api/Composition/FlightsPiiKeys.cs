using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Infrastructure.Privacy;

namespace Travel.Modules.Flights.Api.Composition;

public static class FlightsPiiKeys
{
    public static (int ExitCode, string Code) Initialize(
        IConfiguration configuration,
        TimeProvider time
    )
    {
        try
        {
            var options =
                configuration
                    .GetSection(FlightsPiiProtectionOptions.SectionName)
                    .Get<FlightsPiiProtectionOptions>()
                ?? new();
            using var provider = new FlightsPiiProtectionProvider(Options.Create(options), time);
            var result = provider.Initialize();
            return result.IsError ? (3, result.FirstError.Code) : (0, "PiiKeyRingInitialized");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (3, "Flights.PiiProtectionUnavailable");
        }
    }
}
