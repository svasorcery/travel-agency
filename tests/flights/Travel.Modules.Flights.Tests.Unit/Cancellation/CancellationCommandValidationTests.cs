using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Handlers.Cancellation;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationCommandValidationTests
{
    [Theory]
    [InlineData("prepare")]
    [InlineData("consent")]
    [InlineData("abandon")]
    [InlineData("status")]
    [InlineData("refresh")]
    public async Task Invalid_identity_is_rejected_before_opening_persistence(string action)
    {
        var ct = TestContext.Current.CancellationToken;
        var result = action switch
        {
            "prepare" => await PrepareCancellationHandler.Handle(
                new(Guid.Empty, Guid.Empty, Guid.Empty, 0, ""),
                null!,
                null!,
                TimeProvider.System,
                ct
            ),
            "consent" => await ConsentCancellationHandler.Handle(
                new(Guid.Empty, Guid.Empty, Guid.Empty, 0, 0, "", "", true, ""),
                null!,
                null!,
                TimeProvider.System,
                ct
            ),
            "abandon" => await AbandonCancellationHandler.Handle(
                new(Guid.Empty, Guid.Empty, Guid.Empty, 0),
                null!,
                null!,
                TimeProvider.System,
                ct
            ),
            "refresh" => await RefreshCancellationHandler.Handle(
                new(Guid.Empty, Guid.Empty, Guid.Empty, 0, Guid.Empty, false),
                null!,
                null!,
                TimeProvider.System,
                ct
            ),
            _ => await GetCancellationStatusHandler.Handle(
                new(Guid.Empty, Guid.Empty),
                null!,
                TimeProvider.System,
                ct
            ),
        };
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.CancellationCommandInvalid");
    }
}
