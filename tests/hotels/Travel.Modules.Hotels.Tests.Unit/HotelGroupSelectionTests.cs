using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelGroupSelectionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("leaf", "group")]
    public async Task Exactly_one_rates_selector_is_required_before_source_access(
        string? leaf,
        string? group
    )
    {
        var service = new HotelSearchService([], new(TimeProvider.System), TimeProvider.System);
        (
            await service.RatesAsync(
                Guid.NewGuid(),
                leaf,
                TestContext.Current.CancellationToken,
                group
            )
        ).FirstError.ShouldBe(HotelErrors.Invalid);
    }
}
