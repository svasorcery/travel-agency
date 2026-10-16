using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

public sealed class ConvergenceFaultOrderingTests
{
    [Fact]
    public async Task Releasing_projection_retry_does_not_release_notification_before_projection_commits()
    {
        var id = Guid.NewGuid();
        var envelope = new Envelope(new OrderConfirmedNotification(id, Guid.NewGuid(), 10));
        var context = DispatchProxy.Create<IMessageContext, EnvelopeContext>();
        ((EnvelopeContext)(object)context).Envelope = envelope;
        var faults = new ConvergenceFaults { Armed = true, AggregateId = id };
        faults.Notifications.Enqueue((envelope.Id, 10));
        faults.ReleaseRetry.TrySetResult();

        var delivery = ConvergenceNotificationMiddleware.BeforeAsync(
            context,
            faults,
            CancellationToken.None
        );
        try
        {
            delivery.IsCompleted.ShouldBeFalse();
            faults.ReleaseNotifications.TrySetResult();
            await delivery;
        }
        finally
        {
            faults.ReleaseAll();
        }
    }

    public class EnvelopeContext : DispatchProxy
    {
        public Envelope Envelope { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_Envelope" ? Envelope : throw new NotSupportedException();
    }
}
