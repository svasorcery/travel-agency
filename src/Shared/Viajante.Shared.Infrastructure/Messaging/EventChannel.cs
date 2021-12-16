using System.Threading.Channels;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal sealed class EventChannel : IEventChannel
    {
        private readonly Channel<IEvent> _messages = Channel.CreateUnbounded<IEvent>();

        public ChannelReader<IEvent> Reader => _messages.Reader;
        public ChannelWriter<IEvent> Writer => _messages.Writer;
    }
}
