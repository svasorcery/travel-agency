using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.ReadModels;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationDeliveryPolicyTests
{
    [Fact]
    public void Work_storage_failures_retry_boundedly_but_source_corruption_goes_to_dlq()
    {
        Type[] work =
        [
            typeof(ExecuteCancellationPreparation),
            typeof(ExecuteCancellationConfirmation),
            typeof(ObserveCancellation),
            typeof(AdmissionDeadline),
            typeof(RecoveryDeadline),
            typeof(ConfirmationBarrierDeadline),
        ];
        var chains = work.Select(type => new HandlerChain(type, new HandlerGraph())).ToArray();
        var unrelated = new HandlerChain(typeof(ConfirmOrderCommand), new HandlerGraph());
        new CancellationDeliveryPolicy().Apply(chains.Append(unrelated).ToArray(), null!, null!);
        unrelated.Failures.ShouldBeEmpty();
        var transient = new DbUpdateException(
            "synthetic",
            new PostgresException("synthetic", "ERROR", "ERROR", "40P01")
        );
        foreach (var chain in chains)
        {
            chain.Failures.First(rule => rule.Match.Matches(transient)).Count().ShouldBe(4);
            chain
                .Failures.First(rule => rule.Match.Matches(new BookingWriteConflictException()))
                .Count()
                .ShouldBe(4);
            chain
                .Failures.First(rule =>
                    rule.Match.Matches(new BookingSourceOwnershipMissingException())
                )
                .Count()
                .ShouldBe(1);
            chain
                .Failures.First(rule =>
                    rule.Match.Matches(new BookingProjectionTerminalException())
                )
                .Count()
                .ShouldBe(1);
        }
    }
}
