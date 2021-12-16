using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Humanizer;
using Viajante.Shared.Abstractions.Events;
using Viajante.Shared.Abstractions.Messaging;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal sealed class InMemoryMessageBroker : IMessageBroker
    {
        private readonly IAsyncEventDispatcher _asyncEventDispatcher;
        private readonly ILogger<InMemoryMessageBroker> _logger;

        public InMemoryMessageBroker(IAsyncEventDispatcher asyncEventDispatcher, ILogger<InMemoryMessageBroker> logger)
        {
            _asyncEventDispatcher = asyncEventDispatcher;
            _logger = logger;
        }

        public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken = default)
        {
            var name = @event.GetType().Name.Underscore();
            _logger.LogInformation("Publishing an event: {Name}...", name);
            await _asyncEventDispatcher.PublishAsync(@event, cancellationToken);
        }
    }
}
