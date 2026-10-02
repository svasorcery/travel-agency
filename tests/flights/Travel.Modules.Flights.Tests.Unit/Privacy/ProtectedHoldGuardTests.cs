using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class ProtectedHoldGuardTests
{
    [Fact]
    public async Task Legacy_command_without_envelope_is_rejected_before_any_dependency()
    {
        var result = await HoldOfferHandler.Handle(
            new HoldOfferCommand(Guid.NewGuid(), Guid.NewGuid(), null!),
            [],
            null!,
            null!,
            null!,
            TimeProvider.System,
            NullLogger<HoldOfferCommand>.Instance,
            null!,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PiiEnvelopeInvalid");
    }
}
