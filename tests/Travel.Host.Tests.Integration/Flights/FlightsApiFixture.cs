using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Idempotency;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// xUnit class fixture that hosts the Flights HTTP endpoints in a lean in-memory
/// <see cref="TestServer"/>. It reproduces the real <c>Travel.Host</c> request pipeline —
/// authentication, the <c>RequireAuthenticatedUser</c> fallback policy, the per-endpoint
/// <c>[Authorize]</c> / <c>[AllowAnonymous]</c> intent, the idempotency middleware, routing
/// and model binding — but stubs the message bus (<see cref="FakeMessageBus"/>) and the
/// idempotency store, so no Postgres / NATS / Wolverine runtime is required. The host boots
/// once per test class.
/// </summary>
public sealed class FlightsApiFixture : IAsyncLifetime
{
    private WebApplication _app = default!;

    public HttpClient Client { get; private set; } = default!;

    public FakeMessageBus Bus { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton<IMessageBus>(Bus);
        builder.Services.AddSingleton<IIdempotencyStore>(new FakeIdempotencyStore());

        // Test auth scheme + the same fallback policy Program.cs applies.
        builder
            .Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { }
            );
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(
                "flights:book",
                p =>
                    p.RequireAuthenticatedUser()
                        .RequireAssertion(ctx =>
                        {
                            var scopeClaim =
                                ctx.User.FindFirst("scope")?.Value
                                ?? ctx.User.FindFirst("scp")?.Value;
                            return scopeClaim?.Split(' ').Contains("flights:book") == true;
                        })
            );
        });

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseMiddleware<IdempotencyKeyMiddleware>();

        MapFlightsEndpoints(_app);

        await _app.StartAsync();
        Client = _app.GetTestServer().CreateClient();
    }

    /// <summary>
    /// Maps the Flights endpoints with the same routes and the same authorization intent as
    /// the <c>[WolverinePost]/[WolverineGet]</c> + <c>[Authorize]/[AllowAnonymous]</c>
    /// attributes on the endpoint classes in <c>Travel.Modules.Flights.Api</c>.
    /// </summary>
    private static void MapFlightsEndpoints(WebApplication app)
    {
        app.MapPost("/api/flights/search", SearchEndpoint.Post).AllowAnonymous();
        app.MapPost("/api/flights/search/nl", NlSearchEndpoint.Post).AllowAnonymous();
        app.MapPost("/api/flights/orders/quote", QuoteOfferEndpoint.Post).AllowAnonymous();

        app.MapPost("/api/flights/orders/hold", HoldOfferEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/orders/confirm", ConfirmOrderEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/orders/{aggregateId:guid}/cancel", CancelOrderEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapGet("/api/flights/orders/{aggregateId:guid}", GetOrderEndpoint.Get)
            .RequireAuthorization();
        app.MapGet("/api/flights/orders", ListOrdersEndpoint.Get).RequireAuthorization();
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
