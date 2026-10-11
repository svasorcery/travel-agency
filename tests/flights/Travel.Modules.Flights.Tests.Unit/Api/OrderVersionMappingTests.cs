using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;

namespace Travel.Modules.Flights.Tests.Unit.Api;

public sealed class OrderVersionMappingTests
{
    [Theory]
    [InlineData(-1L, null)]
    [InlineData(0L, null)]
    [InlineData(12L, 12L)]
    public void Historical_checkpoint_is_unknown_not_an_invalid_wire_version(
        long source,
        long? expected
    )
    {
        var view = new OrderView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ord_fictional",
            "Held",
            10m,
            "GBP",
            "{}",
            [],
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            1,
            source
        );
        OrderResponseMapper.From(view).ProjectedStreamVersion.ShouldBe(expected);
    }
}
