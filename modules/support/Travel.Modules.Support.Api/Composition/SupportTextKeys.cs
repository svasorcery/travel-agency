using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Travel.Modules.Support.Infrastructure.Privacy;

namespace Travel.Modules.Support.Api.Composition;

/// <summary>Explicit key-only administration facade; normal module startup never calls it.</summary>
public static class SupportTextKeys
{
    public static (int ExitCode, string Code) Initialize(
        IConfiguration configuration,
        TimeProvider time
    )
    {
        try
        {
            var settings =
                configuration
                    .GetSection(SupportTextProtectionOptions.SectionName)
                    .Get<SupportTextProtectionOptions>()
                ?? new();
            using var provider = new SupportTextProtectionProvider(Options.Create(settings), time);
            var result = provider.Initialize();
            return result.IsError ? (1, result.FirstError.Code) : (0, "Support.KeysInitialized");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (1, "Support.ProtectionUnavailable");
        }
    }
}
