using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Viajante.Shared.Infrastructure.Swagger
{
    internal static class Extensions
    {
        public static IServiceCollection AddSwagger(this IServiceCollection services, IConfiguration configuration)
        {
            var appOptions = new ApplicationOptions();
            configuration.GetSection("application").Bind(appOptions);

            services.AddSwaggerGen(swagger =>
            {
                swagger.EnableAnnotations();
                swagger.CustomSchemaIds(x => x.FullName);
                swagger.SwaggerDoc(appOptions.ApiVersion, new OpenApiInfo
                {
                    Title = appOptions.ApiTitle,
                    Version = appOptions.ApiVersion
                });
            });

            return services;
        }

        public static IApplicationBuilder UseSwagger(this IApplicationBuilder app, IConfiguration configuration)
        {
            var appOptions = new ApplicationOptions();
            configuration.GetSection("application").Bind(appOptions);

            app
                .UseSwagger()
                .UseReDoc(reDoc =>
                {
                    reDoc.RoutePrefix = "docs";
                    reDoc.SpecUrl($"/swagger/{appOptions.ApiVersion}/swagger.json");
                    reDoc.DocumentTitle = appOptions.ApiTitle;
                })
                .UseSwaggerUI(c => c.SwaggerEndpoint($"/swagger/{appOptions.ApiVersion}/swagger.json", appOptions.ApiTitle))
                .UseRouting();

            return app;
        }
    }
}
