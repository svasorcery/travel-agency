using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal sealed class EventDispatcherJob : BackgroundService
    {
        private readonly IEventChannel _eventChannel;
        private readonly IEventDispatcher _eventDispatcher;
        private readonly ILogger<EventDispatcherJob> _logger;

        public EventDispatcherJob(IEventChannel eventChannel, IEventDispatcher eventDispatcher, ILogger<EventDispatcherJob> logger)
        {
            _eventChannel = eventChannel;
            _eventDispatcher = eventDispatcher;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _eventDispatcher.PublishAsync(@event, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, exception.Message);
                }
            }
        }
    }
}
