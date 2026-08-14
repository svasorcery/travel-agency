using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Identity.Infrastructure;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration config,
        IWebHostEnvironment env
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
                    jwt.RequireHttpsMetadata = !env.IsDevelopment();
                }
            );

        services.AddAuthorization();
        return services;
    }
}
