using System.Reflection;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Infrastructure.Diagnostics;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationReplaySafetyTests
{
    [Fact]
    public void Diagnostics_allow_only_readonly_recovery_and_never_replay_dispatch_commands()
    {
        var allowed =
            (HashSet<string>)
                typeof(BookingConsistencyDiagnostics)
                    .GetField("AllowedTypes", BindingFlags.Static | BindingFlags.NonPublic)!
                    .GetValue(null)!;
        foreach (
            var type in new[]
            {
                typeof(ObserveCancellation),
                typeof(AdmissionDeadline),
                typeof(RecoveryDeadline),
                typeof(ConfirmationBarrierDeadline),
            }
        )
            allowed.ShouldContain(type.FullName!);
        foreach (
            var type in new[]
            {
                typeof(ExecuteCancellationPreparation),
                typeof(ExecuteCancellationConfirmation),
                typeof(PrepareCancellationCommand),
                typeof(ConsentCancellationCommand),
                typeof(ConfirmOrderCommand),
            }
        )
            allowed.ShouldNotContain(type.FullName!);
    }
}
