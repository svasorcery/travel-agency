using System.Reflection;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cancellation;

[Trait("Category", "Integration")]
public sealed class ConfirmationCoordinationTests(CancellationTestFixture fixture)
    : IClassFixture<CancellationTestFixture>
{
    [Fact]
    public async Task Unmarked_legacy_held_never_enters_supplier_or_wallet_chain()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
        var events = CancellationTestFixture
            .HeldEvents(owner, TimeProvider.System.GetUtcNow())
            .Where(e => e is not BookingMutationCoordinationEnabled)
            .Cast<object>()
            .ToArray();
        session.Events.StartStream<BookingAggregate>(id, events);
        await session.SaveChangesAsync(ct);
        var supplier = DispatchProxy.Create<IFlightBookingProvider, ForbiddenProxy>();
        var wallet = DispatchProxy.Create<IPaymentGateway, ForbiddenProxy>();
        var metrics = DispatchProxy.Create<IFlightsMetrics, ForbiddenProxy>();
        var result = await ConfirmOrderHandler.Handle(
            new(id, owner),
            session,
            [supplier],
            wallet,
            metrics,
            scope.ServiceProvider.GetRequiredService<IMartenOutbox>(),
            TimeProvider.System,
            NullLogger<ConfirmOrderCommand>.Instance,
            new Travel.Modules.Flights.Infrastructure.Cancellation.ProcessDispatchInstanceIdentity(),
            ct
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.LegacyConfirmationUnverified");
        ((ForbiddenProxy)(object)supplier).Calls.ShouldBe(0);
        ((ForbiddenProxy)(object)wallet).Calls.ShouldBe(0);
    }

    public class ForbiddenProxy : DispatchProxy
    {
        public int Calls { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("Unexpected synthetic external call.");
        }
    }
}
