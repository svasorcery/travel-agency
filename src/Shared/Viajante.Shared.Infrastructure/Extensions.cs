using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Dispatchers;
using Viajante.Shared.Infrastructure.Commands;
using Viajante.Shared.Infrastructure.Events;
using Viajante.Shared.Infrastructure.Queries;
using Viajante.Shared.Infrastructure.Dispatchers;

namespace Viajante.Shared.Infrastructure
{
    public static class Extensions
    {
        public static IServiceCollection AddSharedFramework(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddCommands()
                .AddEvents()
                .AddQueries();

            services
                .AddSingleton<IDispatcher, InMemoryDispatcher>();

            return services;
        }
    }
}
