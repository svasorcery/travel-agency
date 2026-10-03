using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class SavedTravelersJwtHttpTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    private static readonly Guid Owner = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ForgedDemoOwner = Guid.Parse(
        "44444444-4444-4444-4444-444444444444"
    );
    private const string Path = "/api/flights/travelers/11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData("GET", 200)]
    [InlineData("PUT", 201)]
    public async Task Signed_Bearer_subject_is_the_owner_without_synthetic_headers(
        string method,
        int status
    )
    {
        Reset();
        var token = ProfileJwtTestTokens.Create(Owner);
        using var request = Request(method, token);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(status);
        (response.Headers.CacheControl?.NoStore).ShouldBe(true);
        fixture.SavedTravelers.Calls.ShouldBe(1);
        fixture.SavedTravelers.Owner.ShouldBe(Owner);
        fixture.SavedTravelers.Owner.ShouldNotBe(ForgedDemoOwner);
        fixture.Bus.InvocationCount.ShouldBe(0);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        await AssertTokenNotLogged(response, token);
    }

    [Theory]
    [InlineData("GET", "signature", 401)]
    [InlineData("PUT", "signature", 401)]
    [InlineData("GET", "audience", 401)]
    [InlineData("PUT", "audience", 401)]
    [InlineData("GET", "issuer", 401)]
    [InlineData("PUT", "issuer", 401)]
    [InlineData("GET", "expired", 401)]
    [InlineData("PUT", "expired", 401)]
    [InlineData("GET", "scope", 403)]
    [InlineData("PUT", "scope", 403)]
    [InlineData("GET", "subject", 401)]
    [InlineData("PUT", "subject", 401)]
    public async Task Invalid_signed_Bearer_never_reaches_service_bus_or_cache(
        string method,
        string failure,
        int status
    )
    {
        Reset();
        var token = ProfileJwtTestTokens.Create(Owner, failure);
        using var request = Request(method, token);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(status);
        (response.Headers.CacheControl?.NoStore).ShouldBe(true);
        fixture.SavedTravelers.Calls.ShouldBe(0);
        fixture.SavedTravelers.Owner.ShouldBe(Guid.Empty);
        fixture.Bus.InvocationCount.ShouldBe(0);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        await AssertTokenNotLogged(response, token);
    }

    private HttpRequestMessage Request(string method, string token)
    {
        fixture.Client.DefaultRequestHeaders.Contains(TestAuthHandler.UserIdHeader).ShouldBeFalse();
        fixture.Client.DefaultRequestHeaders.Contains(TestAuthHandler.ScopesHeader).ShouldBeFalse();
        var request = new HttpRequestMessage(new HttpMethod(method), Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Travel-Demo-Owner", ForgedDemoOwner.ToString("D"));
        if (method == "PUT")
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            request.Content = JsonContent.Create(
                SavedTravelerDetailsDto.From(FakeSavedTravelerService.Details)
            );
        }
        request.Headers.Contains(TestAuthHandler.UserIdHeader).ShouldBeFalse();
        request.Headers.Contains(TestAuthHandler.ScopesHeader).ShouldBeFalse();
        return request;
    }

    private void Reset()
    {
        fixture.SavedTravelers.Reset();
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        fixture.Logs.Messages.Clear();
    }

    private async Task AssertTokenNotLogged(HttpResponseMessage response, string token)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain(token);
        string.Join("\n", fixture.Logs.Messages).ShouldNotContain(token);
    }
}
