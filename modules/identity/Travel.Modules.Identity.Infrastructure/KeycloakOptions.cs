using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Identity.Infrastructure;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}

internal sealed class KeycloakOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<KeycloakOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakOptions options)
    {
        if (!environment.IsProduction())
            return ValidateNonProduction(options);

        if (!TryGetAuthority(options.Authority, out var authority))
            return ValidateOptionsResult.Fail(
                "Production Keycloak Authority must be an absolute HTTPS URI."
            );

        if (authority.Scheme != Uri.UriSchemeHttps || IsLoopbackHost(authority.Host))
            return ValidateOptionsResult.Fail(
                "Production Keycloak Authority must use HTTPS and must not be loopback."
            );

        return string.IsNullOrWhiteSpace(options.Audience)
            ? ValidateOptionsResult.Fail("Production Keycloak Audience is required.")
            : ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult ValidateNonProduction(KeycloakOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Authority))
            return ValidateOptionsResult.Success;

        return TryGetAuthority(options.Authority, out _)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Keycloak Authority must be an absolute HTTP or HTTPS URI when configured."
            );
    }

    private static bool TryGetAuthority(string? value, out Uri authority) =>
        Uri.TryCreate(value, UriKind.Absolute, out authority!)
        && (authority.Scheme == Uri.UriSchemeHttp || authority.Scheme == Uri.UriSchemeHttps);

    private static bool IsLoopbackHost(string host)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || (
                System.Net.IPAddress.TryParse(normalizedHost, out var address)
                && System.Net.IPAddress.IsLoopback(address)
            );
    }
}
