using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application.FlightLinks;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripFlightLinksHttpTests
{
    private const string Path = "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/flight-links";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Module_registers_link_services_only_for_effective_flag(
        bool trips,
        bool links,
        bool expected
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = trips.ToString(),
                ["Trips:FlightLinks:Enabled"] = links.ToString(),
                ["ConnectionStrings:travel"] =
                    "Host=unused;Database=unused;Username=unused;Password=unused",
            }
        );
        builder.AddTripsModule();
        builder
            .Services.Single(d => d.ServiceType == typeof(TripFlightLinksFeature))
            .ImplementationInstance.ShouldBe(new TripFlightLinksFeature(expected));
        builder.Services.Any(d => d.ServiceType == typeof(ITripFlightLinkStore)).ShouldBe(expected);
        builder
            .Services.Any(d => d.ServiceType == typeof(TripFlightLinkService))
            .ShouldBe(expected);
    }

    [Fact]
    public async Task Availability_does_not_depend_on_storage_readiness()
    {
        await using var app = await TripsHttpFixture.Start(flightLinks: true, ready: false);
        using var flag = await app.Client.GetAsync("/API/TRIPS/flight-links/availability/", Ct);
        flag.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await flag.Content.ReadAsStringAsync(Ct));
        data.RootElement.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        using var links = await app.Client.GetAsync(Path, Ct);
        links.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Disabled_links_are_concealed_before_authentication_but_configuration_is_readable()
    {
        await using var app = await TripsHttpFixture.Start(ready: false);
        using var availability = await app.Client.GetAsync(
            "/api/trips/flight-links/availability",
            Ct
        );
        availability.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await availability.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        app.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var result = await app.Client.GetAsync(Path.ToUpperInvariant() + "/", Ct);
        result.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        result.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, 413)]
    [InlineData(true, 504)]
    public async Task Chunked_patch_is_bounded_before_readiness(bool slow, int expected)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .BuildServiceProvider();
        var issuer = TripsHttpFixture.Issuer;
        var ctx = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim("iss", issuer, ClaimValueTypes.String, issuer),
                        new Claim(
                            "sub",
                            TripsHttpFixture.Owner.ToString(),
                            ClaimValueTypes.String,
                            issuer
                        ),
                    ],
                    "fictional"
                )
            ),
        };
        ctx.Request.Path = Path;
        ctx.Request.Method = "PATCH";
        ctx.Request.Headers.IfMatch = "\"fl-0\"";
        ctx.Request.ContentType = "application/json";
        using var input = slow ? (Stream)new SlowBody() : new MemoryStream(new byte[1025]);
        using var output = new MemoryStream();
        ctx.Request.Body = input;
        ctx.Response.Body = output;
        var guard = new TripsRequestGuardMiddleware(
            _ => throw new InvalidOperationException("Unexpected dispatch."),
            new TripsFeature(true)
        );
        await guard.InvokeAsync(ctx).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        ctx.Response.StatusCode.ShouldBe(expected);
    }

    private sealed class SlowBody : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    [Theory]
    [InlineData(null, "{}", 428)]
    [InlineData("\"1\"", "{}", 400)]
    [InlineData("W/\"fl-0\"", "{}", 400)]
    [InlineData("\"fl-00\"", "{}", 400)]
    [InlineData("\"fl-0\"", "[{}]", 400)]
    [InlineData(
        "\"fl-0\"",
        "{\"op\":\"add\",\"op\":\"remove\",\"orderId\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\"}",
        400
    )]
    public async Task Patch_guards_run_before_storage(string? tag, string body, int status)
    {
        await using var app = await TripsHttpFixture.Start(ready: false, flightLinks: true);
        using var request = new HttpRequestMessage(HttpMethod.Patch, Path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (tag != null)
            request.Headers.TryAddWithoutValidation("If-Match", tag);
        using var response = await app.Client.SendAsync(request, Ct);
        ((int)response.StatusCode).ShouldBe(status);
    }
}
