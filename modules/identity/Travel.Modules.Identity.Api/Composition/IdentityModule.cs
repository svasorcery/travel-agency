using Microsoft.Extensions.Hosting;
using Travel.Modules.Identity.Infrastructure;

namespace Travel.Modules.Identity.Api.Composition;

public static class IdentityModule
{
    public static IHostApplicationBuilder AddIdentityModule(this IHostApplicationBuilder builder)
    {
        builder.Services.AddIdentityInfrastructure(builder.Configuration, builder.Environment);
        return builder;
    }
}
