using System.Reflection;
using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
using Wolverine;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationCommitOrderingTests
{
    [Fact]
    public async Task Scheduled_work_notifications_and_reconcile_are_enrolled_before_one_save()
    {
        var calls = new List<string>();
        var session = DispatchProxy.Create<IDocumentSession, RecordingProxy>();
        var sender = DispatchProxy.Create<IMartenOutbox, RecordingProxy>();
        var store = (RecordingProxy)(object)session;
        var outbox = (RecordingProxy)(object)sender;
        store.Calls = outbox.Calls = calls;
        var due = CancellationTestData.Now.AddSeconds(60);
        var work = new object();
        var notification = new object();
        await session.SaveBookingWithWorkAsync(
            sender,
            Guid.NewGuid(),
            [new(work, due)],
            [notification],
            TestContext.Current.CancellationToken
        );
        calls.ShouldBe([
            "Enroll",
            "PublishAsync",
            "PublishAsync",
            "PublishAsync",
            "SaveChangesAsync",
        ]);
        outbox.Published.Count.ShouldBe(3);
        outbox.Published[0].Message.ShouldBeSameAs(work);
        outbox.Published[0].Options!.ScheduledTime.ShouldBe(due);
        outbox.Published[1].Message.ShouldBeSameAs(notification);
        outbox.Published[2].Message.ShouldBeOfType<ReconcileOrderReadModel>();
    }

    [Fact]
    public async Task Enqueue_failure_prevents_commit_and_does_not_attempt_another_save()
    {
        var calls = new List<string>();
        var session = DispatchProxy.Create<IDocumentSession, RecordingProxy>();
        var sender = DispatchProxy.Create<IMartenOutbox, RecordingProxy>();
        var store = (RecordingProxy)(object)session;
        var outbox = (RecordingProxy)(object)sender;
        store.Calls = outbox.Calls = calls;
        outbox.FailPublish = true;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            session.SaveBookingWithWorkAsync(
                sender,
                Guid.NewGuid(),
                [new(new object(), null)],
                [],
                TestContext.Current.CancellationToken
            )
        );
        calls.ShouldBe(["Enroll", "PublishAsync"]);
    }

    public class RecordingProxy : DispatchProxy
    {
        internal List<string> Calls { get; set; } = [];
        internal List<(object Message, DeliveryOptions? Options)> Published { get; } = [];
        internal bool FailPublish { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            if (method.Name == "Enroll")
                return null;
            if (method.Name == "PublishAsync")
            {
                Published.Add((args![0]!, args.Length > 1 ? args[1] as DeliveryOptions : null));
                if (FailPublish)
                    throw new InvalidOperationException("Synthetic enqueue failure.");
                if (method.ReturnType == typeof(ValueTask))
                    return ValueTask.CompletedTask;
                return Task.CompletedTask;
            }
            if (method.Name == "SaveChangesAsync")
                return Task.CompletedTask;
            throw new NotSupportedException(method.Name);
        }
    }
}
