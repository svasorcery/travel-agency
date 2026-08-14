using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Travel.Host.Configuration;

public sealed class HostConnectionOptions
{
    public string Travel { get; set; } = string.Empty;
    public string Nats { get; set; } = string.Empty;
    public string Redis { get; set; } = string.Empty;
}

public static class HostConnectionOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddHostConnectionOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        services
            .AddOptions<HostConnectionOptions>()
            .Configure(options =>
            {
                options.Travel = configuration.GetConnectionString("travel") ?? string.Empty;
                options.Nats = configuration.GetConnectionString("nats") ?? string.Empty;
                options.Redis = configuration.GetConnectionString("redis") ?? string.Empty;
            })
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<HostConnectionOptions>>(
            new HostConnectionOptionsValidator(environment)
        );
        return services;
    }
}

internal sealed class HostConnectionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<HostConnectionOptions>
{
    public ValidateOptionsResult Validate(string? name, HostConnectionOptions options)
    {
        if (!environment.IsProduction())
            return ValidateOptionsResult.Success;

        if (!TryGetPostgresHost(options.Travel, out var postgresHost) || IsLoopback(postgresHost))
            return ValidateOptionsResult.Fail(
                "Production travel connection must be explicit and non-loopback."
            );

        if (!TryGetServiceHost(options.Nats, "nats", out var natsHost) || IsLoopback(natsHost))
            return ValidateOptionsResult.Fail(
                "Production nats connection must be explicit and non-loopback."
            );

        if (!TryGetRedisHost(options.Redis, out var redisHost) || IsLoopback(redisHost))
            return ValidateOptionsResult.Fail(
                "Production redis connection must be explicit and non-loopback."
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

    private static bool TryGetServiceHost(string value, string scheme, out string host)
    {
        host = string.Empty;
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, scheme, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        host = uri.Host;
        return !string.IsNullOrWhiteSpace(host);
    }

    private static bool TryGetRedisHost(string value, out string host)
    {
        host = string.Empty;
        var endpoint = value.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        if (!endpoint.Contains("://", StringComparison.Ordinal))
            endpoint = $"redis://{endpoint}";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;

        host = uri.Host;
        return !string.IsNullOrWhiteSpace(host);
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
