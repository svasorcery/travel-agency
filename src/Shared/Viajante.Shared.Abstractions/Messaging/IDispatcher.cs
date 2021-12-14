using System.Threading;
using System.Threading.Tasks;
using Viajante.Shared.Abstractions.Commands;
using Viajante.Shared.Abstractions.Events;
using Viajante.Shared.Abstractions.Queries;

namespace Viajante.Shared.Abstractions.Messaging
{
    public interface IDispatcher
    {
        Task SendAsync<T>(T command, CancellationToken cancellationToken = default) where T : class, ICommand;
        Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default) where T : class, IEvent;
        Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
    }
}
