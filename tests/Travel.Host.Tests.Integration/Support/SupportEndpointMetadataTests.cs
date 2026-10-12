using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Application;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

public sealed class SupportEndpointMetadataTests
{
    [Theory]
    [InlineData(typeof(CreateSupportCaseEndpoint), "Put", false)]
    [InlineData(typeof(GetSupportCaseEndpoint), "Get", false)]
    [InlineData(typeof(ListSupportCasesEndpoint), "Get", false)]
    [InlineData(typeof(ListSupportMessagesEndpoint), "Get", false)]
    [InlineData(typeof(ReplySupportCaseEndpoint), "Put", false)]
    [InlineData(typeof(GetSupportReceiptEndpoint), "Get", false)]
    [InlineData(typeof(OperatorGetSupportCaseEndpoint), "Get", true)]
    [InlineData(typeof(OperatorListSupportCasesEndpoint), "Get", true)]
    [InlineData(typeof(OperatorListSupportMessagesEndpoint), "Get", true)]
    [InlineData(typeof(OperatorReplySupportCaseEndpoint), "Put", true)]
    [InlineData(typeof(OperatorGetSupportReceiptEndpoint), "Get", true)]
    [InlineData(typeof(ActSupportCaseEndpoint), "Put", true)]
    public void Endpoints_have_correct_actor_policy_and_direct_service_without_durable_bus(
        Type endpoint,
        string method,
        bool op
    )
    {
        var entry = endpoint.GetMethod(method)!;
        var auth = entry.GetCustomAttribute<AuthorizeAttribute>();
        auth.ShouldNotBeNull();
        auth.Policy.ShouldBe(op ? SupportModule.OperatorPolicy : null);
        entry
            .GetParameters()
            .Single(x => x.ParameterType == typeof(ISupportCaseService))
            .GetCustomAttribute<FromServicesAttribute>()
            .ShouldNotBeNull();
        entry
            .GetParameters()
            .ShouldNotContain(x => x.ParameterType == typeof(Wolverine.IMessageBus));
    }

    [Fact]
    public void Text_DTO_debug_strings_never_include_private_text()
    {
        new CreateSupportCaseDto(Guid.NewGuid(), "private", "private")
            .ToString()
            .ShouldBe(nameof(CreateSupportCaseDto));
        new ReplySupportCaseDto("private", 1, "Reply")
            .ToString()
            .ShouldBe(nameof(ReplySupportCaseDto));
        new SupportMessageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Customer",
            "private",
            1,
            DateTimeOffset.UtcNow
        )
            .ToString()
            .ShouldBe(nameof(SupportMessageDto));
    }
}
