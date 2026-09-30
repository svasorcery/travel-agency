using System.Text.Json;
using Shouldly;
using Xunit;

namespace Travel.Modules.Identity.Tests.Unit.Configuration;

public sealed class KeycloakRealmConfigurationTests
{
    [Fact]
    public void Travel_web_uses_the_basic_client_scope_for_access_token_subject()
    {
        var realmPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "travel-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        var client = realm
            .RootElement.GetProperty("clients")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("clientId").GetString() == "travel-web");

        client
            .GetProperty("defaultClientScopes")
            .EnumerateArray()
            .Select(scope => scope.GetString())
            .ShouldContain("basic");
    }

    [Fact]
    public void Basic_scope_maps_subject_into_the_access_token()
    {
        var realmPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "travel-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        var basicScope = realm
            .RootElement.GetProperty("clientScopes")
            .EnumerateArray()
            .Single(scope => scope.GetProperty("name").GetString() == "basic");
        var subjectMapper = basicScope
            .GetProperty("protocolMappers")
            .EnumerateArray()
            .Single(mapper =>
                mapper.GetProperty("protocolMapper").GetString() == "oidc-sub-mapper"
            );

        subjectMapper
            .GetProperty("config")
            .GetProperty("access.token.claim")
            .GetString()
            .ShouldBe("true");
    }

    [Fact]
    public void Travel_web_access_tokens_include_the_backend_audience()
    {
        var realmPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "travel-realm.json");
        using var realm = JsonDocument.Parse(File.ReadAllText(realmPath));

        var client = realm
            .RootElement.GetProperty("clients")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("clientId").GetString() == "travel-web");
        var audienceMapper = client
            .GetProperty("protocolMappers")
            .EnumerateArray()
            .Single(mapper =>
                mapper.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"
            );
        var mapperConfig = audienceMapper.GetProperty("config");

        mapperConfig.GetProperty("included.client.audience").GetString().ShouldBe("travel-web");
        mapperConfig.GetProperty("access.token.claim").GetString().ShouldBe("true");
        mapperConfig.GetProperty("id.token.claim").GetString().ShouldBe("false");
    }
}
