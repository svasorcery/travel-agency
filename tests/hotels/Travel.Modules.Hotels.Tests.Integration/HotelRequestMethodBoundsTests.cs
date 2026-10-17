using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Api.Middleware;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelRequestMethodBoundsTests
{
    [Theory]
    [InlineData("POST", "POST")]
    [InlineData("pOsT", "POST")]
    [InlineData("PUT", "PUT")]
    [InlineData("pUt", "PUT")]
    public async Task Routed_method_variants_cannot_bypass_the_body_bound_before_JSON_binding(
        string method,
        string routeMethod
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ApplicationName = typeof(HotelRequestMethodBoundsTests).Assembly.FullName,
            }
        );
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IOptions<HotelRequestOptions>>(
            Options.Create(new HotelRequestOptions())
        );
        await using var app = builder.Build();
        string? receivedMethod = null;
        var entered = 0;
        var boundLength = 0;
        app.Use(
            async (context, next) =>
            {
                receivedMethod = context.Request.Method;
                await next(context);
            }
        );
        app.UseMiddleware<HotelsRequestPolicyMiddleware>();
        app.MapMethods(
            "/api/hotels/guard",
            [routeMethod],
            (JsonElement body) =>
            {
                entered++;
                boundLength = body.GetProperty("padding").GetString()!.Length;
                return Results.Ok();
            }
        );
        await app.StartAsync(ct);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/hotels/guard")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { padding = new string('x', 16385) }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        using var response = await app.GetTestClient().SendAsync(request, ct);
        var observed = JsonSerializer.Serialize(
            new
            {
                method,
                receivedMethod,
                status = (int)response.StatusCode,
                entered,
                boundLength,
            }
        );
        TestContext.Current.TestOutputHelper?.WriteLine(observed);
        receivedMethod.ShouldBe(method, observed);
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge, observed);
        entered.ShouldBe(0, observed);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }
}
