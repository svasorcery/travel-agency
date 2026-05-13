using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = config["Keycloak:Authority"];
                options.Audience = config["Keycloak:Audience"] ?? "travel-web";
                options.RequireHttpsMetadata = !env.IsDevelopment();
            });

        services.AddAuthorization();
        return services;
    }
}
