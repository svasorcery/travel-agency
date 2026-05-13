using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Travel.Modules.Identity.Infrastructure;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Keycloak:Authority"];
                options.Audience = config["Keycloak:Audience"] ?? "travel-web";
                options.RequireHttpsMetadata = false; // dev only; flip to true in production via config
            });

        services.AddAuthorization();
        return services;
    }
}
