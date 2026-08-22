using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Travel.Modules.Identity.Infrastructure.Authentication;

namespace Travel.Modules.Identity.Infrastructure;

internal static class IdentityServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment env
    )
    {
        services
            .AddOptions<KeycloakOptions>()
            .Bind(config.GetSection(KeycloakOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeycloakOptions>>(new KeycloakOptionsValidator(env));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakOptions>>(
                (jwt, keycloak) =>
                {
                    jwt.Authority = keycloak.Value.Authority;
                    jwt.Audience = keycloak.Value.Audience;
                    jwt.MapInboundClaims = false;
                    jwt.RequireHttpsMetadata = !env.IsDevelopment();
                }
            );
        services.AddTransient<IClaimsTransformation, NormalizedIdentityClaimsTransformation>();

        services.AddAuthorization();
        return services;
    }
}
