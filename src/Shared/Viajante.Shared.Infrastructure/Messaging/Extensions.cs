using Microsoft.Extensions.DependencyInjection;
using Viajante.Shared.Abstractions.Messaging;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal static class Extensions
    {
        public static IServiceCollection AddMessaging(this IServiceCollection services)
            => services
                .AddTransient<IMessageBroker, InMemoryMessageBroker>()
                .AddTransient<IAsyncEventDispatcher, AsyncEventDispatcher>()
                .AddSingleton<IEventChannel, EventChannel>()
                .AddHostedService<EventDispatcherJob>();
    }
}
