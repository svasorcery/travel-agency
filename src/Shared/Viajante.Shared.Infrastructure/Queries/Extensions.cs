using System;
using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Queries;

namespace Viajante.Shared.Infrastructure.Queries
{
    internal static class Extensions
    {
        public static IServiceCollection AddQueries(this IServiceCollection services)
            => services
                .AddSingleton<IQueryDispatcher, QueryDispatcher>()
                .Scan(s => s.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                    .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)))
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                );
    }
}
