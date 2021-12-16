using System.Threading.Channels;
using Viajante.Shared.Abstractions.Events;

namespace Viajante.Shared.Infrastructure.Messaging
{
    internal interface IEventChannel
    {
        ChannelReader<IEvent> Reader { get; }
        ChannelWriter<IEvent> Writer { get; }
    }
}
