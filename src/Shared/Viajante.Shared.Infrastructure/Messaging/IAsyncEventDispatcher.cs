using System.Threading;
using System.Threading.Tasks;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal interface IAsyncEventDispatcher
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent;
    }
}
