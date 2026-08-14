using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Travel.AI.Configuration;

public sealed class AiConnectionOptions
{
    public string Travel { get; set; } = string.Empty;
    public string Nats { get; set; } = string.Empty;
}

public static class AiConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddTravelAiOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        services
            .AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnthropicOptions>>(
            new AnthropicOptionsValidator(environment)
        );

        services
            .AddOptions<AiConnectionOptions>()
            .Configure(options =>
            {
                options.Travel = configuration.GetConnectionString("travel") ?? string.Empty;
                options.Nats = configuration.GetConnectionString("nats") ?? string.Empty;
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AiConnectionOptions>>(
            new AiConnectionOptionsValidator(environment)
        );
        return services;
    }
}

internal sealed class AiConnectionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AiConnectionOptions>
{
    public ValidateOptionsResult Validate(string? name, AiConnectionOptions options)
    {
        if (!environment.IsProduction())
            return ValidateOptionsResult.Success;

        if (!TryGetPostgresHost(options.Travel, out var postgresHost) || IsLoopback(postgresHost))
            return ValidateOptionsResult.Fail(
                "Production travel connection must be explicit and non-loopback."
            );

        if (
            !Uri.TryCreate(options.Nats, UriKind.Absolute, out var natsUri)
            || !string.Equals(natsUri.Scheme, "nats", StringComparison.OrdinalIgnoreCase)
            || IsLoopback(natsUri.Host)
        )
            return ValidateOptionsResult.Fail(
                "Production nats connection must be explicit and non-loopback."
            );

        return ValidateOptionsResult.Success;
    }

    private static bool TryGetPostgresHost(string value, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            host = new NpgsqlConnectionStringBuilder(value).Host ?? string.Empty;
            return !string.IsNullOrWhiteSpace(host);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsLoopback(string host)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || (
                System.Net.IPAddress.TryParse(normalizedHost, out var address)
                && System.Net.IPAddress.IsLoopback(address)
            );
    }
}
