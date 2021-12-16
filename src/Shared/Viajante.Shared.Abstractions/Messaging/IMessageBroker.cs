using System.Threading;
using System.Threading.Tasks;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Abstractions.Messaging
{
    public interface IMessageBroker
    {
        Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default);
    }
}
