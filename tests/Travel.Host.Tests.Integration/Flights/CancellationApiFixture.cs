using System.Reflection;
using ErrorOr;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Identity.Infrastructure.Authentication;
using Travel.Shared.Web;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// Lean HTTP boundary only. No Program, database, schema initializer, PII fixture or supplier registration.
public sealed class CancellationApiFixture : IAsyncLifetime
{
    private WebApplication app = default!;
    public HttpClient Client { get; private set; } = default!;
    public CancellationBusProbe Probe { get; private set; } = default!;
    public static readonly Guid Aggregate = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Owner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    internal static IReadOnlyList<string> FixtureRoutePaths { get; } =
    [
        "/api/flights/orders/{aggregateId}/cancellation",
        "/api/flights/orders/{aggregateId}/cancellation-review",
        "/api/flights/cancellations/prepare",
        "/api/flights/cancellations/consent",
        "/api/flights/cancellations/abandon",
        "/api/flights/cancellations/refresh",
        "/api/flights/cancellations/review/refresh",
        "/api/flights/cancellations/review/resolve",
    ];

    internal static IReadOnlyList<string> CustomerRefundFixtureRoutePaths { get; } =
    [
        "/api/flights/orders/{aggregateId:guid}/customer-refund",
        "/api/flights/orders/{aggregateId:guid}/customer-refund/review",
    ];

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var bus = DispatchProxy.Create<IMessageBus, CancellationBusProbe>();
        Probe = (CancellationBusProbe)(object)bus;
        Probe.Reset();
        builder.Services.AddSingleton(bus);
        builder.Services.AddProblemDetails();
        builder
            .Services.AddAuthentication("CancellationJwt")
            .AddJwtBearer(
                "CancellationJwt",
                options =>
                {
                    options.MapInboundClaims = false;
                    options.IncludeErrorDetails = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = ProfileJwtTestTokens.Issuer,
                        ValidateAudience = true,
                        ValidAudience = ProfileJwtTestTokens.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = ProfileJwtTestTokens.ValidationKey(),
                    };
                }
            );
        builder.Services.AddTransient<
            IClaimsTransformation,
            NormalizedIdentityClaimsTransformation
        >();
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            foreach (
                var scope in new[]
                {
                    "flights:book",
                    "flights:cancellation-review",
                    "flights:customer-refund-review",
                }
            )
                options.AddPolicy(
                    scope,
                    policy => policy.RequireAuthenticatedUser().RequireClaim("scope", scope)
                );
        });
        app = builder.Build();
        app.UseFlightsResponsePolicy();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<CancellationRequestBodyGuard>();
        app.UseMiddleware<CustomerRefundRequestBodyGuard>();
        app.MapGet(
                "/api/flights/orders/{aggregateId}/cancellation",
                GetCancellationStatusEndpoint.Get
            )
            .RequireAuthorization("flights:book");
        app.MapGet(
                "/api/flights/orders/{aggregateId}/cancellation-review",
                GetCancellationReviewEndpoint.Get
            )
            .RequireAuthorization("flights:cancellation-review");
        app.MapPost("/api/flights/cancellations/prepare", PrepareCancellationEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/cancellations/consent", ConsentCancellationEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/cancellations/abandon", AbandonCancellationEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/cancellations/refresh", RefreshCancellationEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost(
                "/api/flights/cancellations/review/refresh",
                RefreshCancellationReviewEndpoint.Post
            )
            .RequireAuthorization("flights:cancellation-review");
        app.MapPost(
                "/api/flights/cancellations/review/resolve",
                ResolveCancellationReviewEndpoint.Post
            )
            .RequireAuthorization("flights:cancellation-review");
        app.MapGet(
                "/api/flights/orders/{aggregateId}/customer-refund",
                GetCustomerRefundStatusEndpoint.Get
            )
            .RequireAuthorization("flights:book");
        app.MapGet(
                "/api/flights/orders/{aggregateId}/customer-refund/review",
                GetCustomerRefundReviewEndpoint.Get
            )
            .RequireAuthorization("flights:customer-refund-review");
        app.MapPost(
                "/api/flights/orders/{aggregateId}/customer-refund/review",
                ResolveCustomerRefundReviewEndpoint.Post
            )
            .RequireAuthorization("flights:customer-refund-review");
        await app.StartAsync();
        Client = app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }

    public class CancellationBusProbe : DispatchProxy
    {
        public List<object> Calls { get; } = [];
        public bool ForeignOwner { get; set; }
        public CancellationStatusResult? StatusOverride { get; set; }

        public void Reset()
        {
            Calls.Clear();
            ForeignOwner = false;
            StatusOverride = null;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != "InvokeAsync")
                throw new NotSupportedException(method.Name);
            var command = args![0]!;
            Calls.Add(command);
            var status =
                StatusOverride
                ?? new CancellationStatusResult(
                    Aggregate,
                    "Held",
                    3,
                    null,
                    null,
                    true,
                    null,
                    null,
                    new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero)
                );
            var refund = new CustomerRefundStatusResult(
                Aggregate,
                40,
                3,
                "Sent",
                new("80.000000000000000001", "GBP"),
                "OriginalFictionalPaymentMethod",
                "FictionalGateway",
                "None",
                null,
                "NotEstablished",
                "NotEstablished",
                false,
                status.ServerNow,
                null
            );
            var requested = method.GetGenericArguments()[0];
            object result =
                requested == typeof(ErrorOr<CustomerRefundStatusResult>)
                    ? ForeignOwner
                        ? (ErrorOr<CustomerRefundStatusResult>)
                            Error.NotFound("Flights.BookingNotFound", "Booking not found.")
                        : (ErrorOr<CustomerRefundStatusResult>)refund
                    : requested == typeof(ErrorOr<CustomerRefundReviewResult>)
                        ? (ErrorOr<CustomerRefundReviewResult>)
                            new CustomerRefundReviewResult(refund, null, [])
                        : method.GetGenericArguments()[0]
                        == typeof(ErrorOr<CancellationReviewResult>)
                            ? (ErrorOr<CancellationReviewResult>)
                                new CancellationReviewResult(status, null, [])
                            : ForeignOwner
                                ? (ErrorOr<CancellationStatusResult>)
                                    Error.NotFound("Flights.BookingNotFound", "Booking not found.")
                                : (ErrorOr<CancellationStatusResult>)status;
            return typeof(Task)
                .GetMethods()
                .Single(m => m.Name == "FromResult")
                .MakeGenericMethod(method.GetGenericArguments()[0])
                .Invoke(null, [result]);
        }
    }
}
