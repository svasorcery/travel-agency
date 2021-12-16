using System;
using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Infrastructure.Events
{
    internal static class Extensions
    {
        public static IServiceCollection AddEvents(this IServiceCollection services)
            => services
                .AddSingleton<IEventDispatcher, EventDispatcher>()
                .Scan(s => s.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                    .AddClasses(c => c.AssignableTo(typeof(IEventHandler<>)))
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                );
    }
}
