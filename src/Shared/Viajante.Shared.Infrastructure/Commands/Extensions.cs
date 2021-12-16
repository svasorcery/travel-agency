using System;
using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Commands;

namespace Viajante.Shared.Infrastructure.Commands
{
    internal static class Extensions
    {
        public static IServiceCollection AddCommands(this IServiceCollection services)
            => services
                .AddSingleton<ICommandDispatcher, CommandDispatcher>()
                .Scan(s => s.FromAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                    .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)))
                    .AsImplementedInterfaces()
                    .WithScopedLifetime()
                );
    }
}
