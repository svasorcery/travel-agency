using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.SavedTravelers;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class SavedTravelersEndpointMetadataTests
{
    [Theory]
    [InlineData(typeof(ListSavedTravelersEndpoint), "Get")]
    [InlineData(typeof(GetSavedTravelerEndpoint), "Get")]
    [InlineData(typeof(PutSavedTravelerEndpoint), "Put")]
    [InlineData(typeof(DeleteSavedTravelerEndpoint), "Delete")]
    public void Production_endpoints_require_book_scope_and_direct_service_injection(
        Type type,
        string method
    )
    {
        var endpoint = type.GetMethod(method)!;
        (endpoint.GetCustomAttribute<AuthorizeAttribute>()?.Policy).ShouldBe("flights:book");
        var service = endpoint
            .GetParameters()
            .Single(p => p.ParameterType == typeof(ISavedTravelerService));
        service.GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        endpoint
            .GetParameters()
            .ShouldNotContain(p => p.ParameterType == typeof(Wolverine.IMessageBus));
    }

    [Fact]
    public void Profile_contract_ToString_hides_all_PII()
    {
        var details = SavedTravelerDetailsDto.From(FakeSavedTravelerService.Details);
        details.ToString().ShouldBe(nameof(SavedTravelerDetailsDto));
        new SavedTravelerViewDto(Guid.NewGuid(), Guid.NewGuid(), details)
            .ToString()
            .ShouldBe(nameof(SavedTravelerViewDto));
        new SavedTravelerPageDto(
            [new SavedTravelerViewDto(Guid.NewGuid(), Guid.NewGuid(), details)],
            0,
            false
        )
            .ToString()
            .ShouldBe(nameof(SavedTravelerPageDto));
    }
}
