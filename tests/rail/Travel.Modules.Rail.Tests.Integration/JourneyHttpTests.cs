using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Rail.Api.Composition;
using Travel.Modules.Rail.Api.Endpoints;
using Travel.Modules.Rail.Api.Middleware;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Infrastructure;
using Travel.ServiceDefaults.Web;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class JourneyHttpTests
{
    private sealed class PlatformProfileFault : IRailRuntimeProfile
    {
        public RailProfile Current =>
            throw new InvalidOperationException("rail-m2-platform-canary");
    }

    private sealed class PlatformCacheFault : IRailSearchCache
    {
        public RailSearchResult? Get(string key, DateTimeOffset now) =>
            throw new InvalidOperationException("rail-m2-platform-canary");

        public void Set(string key, RailSearchResult result) =>
            throw new InvalidOperationException("Unexpected test write");

        public void Clear() => throw new InvalidOperationException("Unexpected test clear");
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("cache")]
    public async Task Unexpected_platform_dependency_fault_is_sanitized_500_while_input_guards_stay_400_413(
        string dependency
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails(PlatformProblemDetails.Configure);
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Rail:Enabled"] = "true",
                ["Rail:Fictional"] = "true",
                ["Rail:JourneyDetailsEnabled"] = "true",
            }
        );
        builder.AddRailModule();
        if (dependency == "profile")
            builder.Services.AddSingleton<IRailRuntimeProfile, PlatformProfileFault>();
        else
            builder.Services.AddSingleton<IRailSearchCache, PlatformCacheFault>();
        await using var app = builder.Build();
        app.UsePlatformWebDefaults();
        app.UseRailResponsePolicy();
        app.MapPost(
            "/api/rail/journey-details",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailJourneyDetailsService service,
                CancellationToken token
            ) => Details(ctx, service, token)
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var malformed = await client.PostAsync(
            "/api/rail/journey-details",
            new StringContent("{\"unknown\":\"input-canary\"}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        malformed.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var oversized = await client.PostAsync(
            "/api/rail/journey-details",
            new StringContent(new string('x', 8193), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        oversized.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var generation = new string('a', 32);
        if (dependency == "cache")
            generation = (
                await app
                    .Services.GetRequiredService<RailReferenceService>()
                    .SuggestAsync("Учебная", TestContext.Current.CancellationToken)
            )
                .Value
                .CatalogueGeneration!;
        var date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var input = new
        {
            search = new
            {
                from = new { source = "yandex-rasp", code = "s1" },
                to = new { source = "yandex-rasp", code = "s2" },
                catalogueGeneration = generation,
                departureDate = date,
                transport = "all",
            },
            expectedProfileEpoch = app.Services.GetRequiredService<RailRuntime>().Current.Epoch,
            selection = new
            {
                key = "opaque-current-key",
                transport = "train",
                departure = date + "T10:00:00+03:00",
                arrival = date + "T12:00:00+03:00",
            },
        };
        using var response = await client.PostAsJsonAsync(
            "/api/rail/journey-details",
            input,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("500");
        body.ShouldNotContain("rail-m2-platform-canary");
        body.ShouldNotContain(nameof(InvalidOperationException));
        body.ShouldNotContain("Rail.Request.Invalid");
        body.ShouldNotContain("provider-failed");
    }

    [Theory]
    [InlineData(true, "complete")]
    [InlineData(false, "skipped")]
    public async Task Lean_actual_endpoints_use_explicit_fictional_transport_and_strict_bound_body(
        bool enabled,
        string kind
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Rail:Enabled"] = "true",
                ["Rail:Fictional"] = "true",
                ["Rail:JourneyDetailsEnabled"] = enabled.ToString(),
            }
        );
        builder.AddRailModule();
        await using var app = builder.Build();
        app.UseMiddleware<RailResponsePolicy>();
        app.MapGet(
            "/api/rail/stations",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailReferenceService service,
                CancellationToken token
            ) => Stations(ctx, service, token)
        );
        app.MapPost(
            "/api/rail/search",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailSearchService service,
                CancellationToken token
            ) => Search(ctx, service, token)
        );
        app.MapPost(
            "/api/rail/journey-details",
            (
                Microsoft.AspNetCore.Http.HttpContext ctx,
                RailJourneyDetailsService service,
                CancellationToken token
            ) => Details(ctx, service, token)
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var stationReply = await client.GetAsync(
            "/api/rail/stations?q=Учебная",
            TestContext.Current.CancellationToken
        );
        using var suggestions = JsonDocument.Parse(
            await stationReply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        suggestions.RootElement.GetProperty("profile").GetString().ShouldBe("fictional");
        var date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        var searchBody = new
        {
            from = new { source = "yandex-rasp", code = "s1" },
            to = new { source = "yandex-rasp", code = "s2" },
            catalogueGeneration = suggestions
                .RootElement.GetProperty("catalogueGeneration")
                .GetString(),
            departureDate = date,
            transport = "all",
        };
        using var search = await client.PostAsJsonAsync(
            "/api/rail/search",
            searchBody,
            TestContext.Current.CancellationToken
        );
        var snapshot = JsonDocument.Parse(
            await search.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var selected = snapshot.RootElement.GetProperty("items")[0];
        var input = new
        {
            search = searchBody,
            expectedProfileEpoch = suggestions.RootElement.GetProperty("profileEpoch").GetString(),
            selection = new
            {
                key = selected.GetProperty("key").GetString(),
                transport = selected.GetProperty("transport").GetString(),
                departure = selected.GetProperty("departure").GetString(),
                arrival = selected.GetProperty("arrival").GetString(),
            },
        };
        using var response = await client.PostAsJsonAsync(
            "/api/rail/journey-details",
            input,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var result = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        result.RootElement.GetProperty("kind").GetString().ShouldBe(kind);
        result.RootElement.GetProperty("stops").GetArrayLength().ShouldBe(enabled ? 3 : 0);
        if (enabled)
        {
            result.RootElement.GetProperty("boardingIndex").GetInt32().ShouldBe(0);
            result
                .RootElement.GetProperty("verification")
                .TryGetProperty("items", out _)
                .ShouldBeFalse();
            var admission = app.Services.GetRequiredService<RailReadAdmission>();
            admission.TryEnter().ShouldBeTrue();
            admission.TryEnter().ShouldBeTrue();
            try
            {
                using var full = await client.PostAsJsonAsync(
                    "/api/rail/journey-details",
                    input,
                    TestContext.Current.CancellationToken
                );
                full.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
                full.Headers.CacheControl!.NoStore.ShouldBeTrue();
            }
            finally
            {
                admission.Release();
                admission.Release();
            }
        }
        var raw = JsonSerializer.Serialize(input, JsonSerializerOptions.Web);
        foreach (
            var invalid in new[]
            {
                raw.Replace(
                    "\"selection\":",
                    "\"unknown\":0,\"selection\":",
                    StringComparison.Ordinal
                ),
                raw.Replace(
                    "\"expectedProfileEpoch\":",
                    "\"expectedProfileEpoch\":\"x\",\"expectedProfileEpoch\":",
                    StringComparison.Ordinal
                ),
            }
        )
        {
            using var bad = await client.PostAsync(
                "/api/rail/journey-details",
                new StringContent(invalid, Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken
            );
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            bad.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }
        using var oversized = await client.PostAsync(
            "/api/rail/journey-details",
            new StringContent(new string('x', 8193), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        oversized.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    private static Task<Microsoft.AspNetCore.Http.IResult> Stations(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailReferenceService service,
        CancellationToken token
    ) => RailEndpoints.Stations(ctx, service, token);

    private static Task<Microsoft.AspNetCore.Http.IResult> Search(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailSearchService service,
        CancellationToken token
    ) => RailEndpoints.Search(ctx, service, token);

    private static Task<Microsoft.AspNetCore.Http.IResult> Details(
        Microsoft.AspNetCore.Http.HttpContext ctx,
        RailJourneyDetailsService service,
        CancellationToken token
    ) => RailEndpoints.JourneyDetails(ctx, service, token);
}
