using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Tests.Unit.Composition;

public sealed class BookingConsistencyHandlerPolicyTests
{
    [Fact]
    public void Facade_registers_a_durable_reconcile_queue()
    {
        using var host = Host.CreateDefaultBuilder()
            .UseWolverine(FlightsModule.ConfigureWolverine)
            .Build();
        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        var endpoint = runtime
            .Options.Transports.AllEndpoints()
            .Single(x => x.Uri == new Uri("local://flights-booking-reconcile/"));
        endpoint.Compile(runtime);
        endpoint.Mode.ShouldBe(EndpointMode.Durable);
        endpoint.Subscriptions.ShouldContain(x => x.Matches(typeof(ReconcileOrderReadModel)));
        runtime
            .Options.Transports.AllEndpoints()
            .Where(x => x.Uri.Scheme == "nats")
            .ShouldNotContain(x =>
                x.Subscriptions.Any(s => s.Matches(typeof(ReconcileOrderReadModel)))
            );
    }

    [Fact]
    public void Rules_are_scoped_and_raw_database_errors_get_bounded_retries()
    {
        var reconcile = Chain(typeof(ReconcileOrderReadModel));
        var webhook = Chain(typeof(ProcessDuffelWebhookCommand));
        var notification = Chain(typeof(OrderConfirmedNotification));
        var ticketed = Chain(typeof(OrderTicketedNotification));
        var cancelledNotification = Chain(typeof(OrderCancelledNotification));
        var unrelated = Chain(typeof(ConfirmOrderCommand));
        new BookingConsistencyHandlerPolicy().Apply(
            [reconcile, webhook, notification, ticketed, cancelledNotification, unrelated],
            null!,
            null!
        );
        unrelated.Failures.ShouldBeEmpty();
        var raw = new DbUpdateException(
            "save",
            new PostgresException("deadlock", "ERROR", "ERROR", "40P01")
        );
        foreach (
            var chain in new[] { reconcile, webhook, notification, ticketed, cancelledNotification }
        )
        {
            var rule = chain.Failures.First(x => x.Match.Matches(raw));
            rule.Count().ShouldBe(4); // Three scheduled retries and terminal DLQ.
        }
        webhook
            .Failures.First(x => x.Match.Matches(new BookingWriteConflictException()))
            .Count()
            .ShouldBe(4);
        webhook
            .Failures.First(x => x.Match.Matches(new BookingCorrelationNotReadyException()))
            .Count()
            .ShouldBe(5);
        webhook
            .Failures.First(x => x.Match.Matches(new BookingSourceOwnershipMissingException()))
            .Count()
            .ShouldBe(1);
        webhook
            .Failures.First(x => x.Match.Matches(new BookingTransitionRejectedException()))
            .Count()
            .ShouldBe(1);
        reconcile
            .Failures.First(x => x.Match.Matches(new InvalidOperationException()))
            .Count()
            .ShouldBe(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new OperationCanceledException("host stopped", raw, cancellation.Token);
        reconcile.Failures.ShouldNotContain(x => x.Match.Matches(cancelled));
    }

    private static HandlerChain Chain(Type messageType) => new(messageType, new HandlerGraph());
}
