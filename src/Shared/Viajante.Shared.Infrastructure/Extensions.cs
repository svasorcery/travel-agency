using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Dispatchers;
using Viajante.Shared.Infrastructure.Exceptions;
using Viajante.Shared.Infrastructure.Commands;
using Viajante.Shared.Infrastructure.Events;
using Viajante.Shared.Infrastructure.Queries;
using Viajante.Shared.Infrastructure.Dispatchers;
using Viajante.Shared.Infrastructure.Messaging;

namespace Viajante.Shared.Infrastructure
{
    public static class Extensions
    {
        public static IServiceCollection AddSharedFramework(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddErrorHandling()
                .AddCommands()
                .AddEvents()
                .AddQueries()
                .AddMessaging();

            services
                .AddControllers();

            services
                .AddSingleton<IDispatcher, InMemoryDispatcher>()
                .AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            return services;
        }

        public static IApplicationBuilder UseSharedFramework(this IApplicationBuilder app, IConfiguration configuration)
        {
            app
                .UseErrorHandling()
                .UseRouting();

            return app;
        }
    }
}
