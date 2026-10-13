using ErrorOr;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Travel.Modules.Identity.Infrastructure.Authentication;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

// Lean HostHTTP boundary. No Program, AppHost, database, initializer, key ring or external service.
public sealed class SupportApiFixture : IAsyncLifetime
{
    private WebApplication app = default!;
    public HttpClient Client { get; private set; } = default!;
    public SupportHttpService Service { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ISupportCaseService>(Service);
        builder.Services.AddSingleton<ISupportClassificationService>(Service);
        builder.Services.AddProblemDetails();
        builder
            .Services.AddAuthentication("SupportJwt")
            .AddJwtBearer(
                "SupportJwt",
                options =>
                {
                    options.MapInboundClaims = false;
                    options.IncludeErrorDetails = false;
                    options.TokenValidationParameters = new()
                    {
                        ValidateIssuer = true,
                        ValidIssuer = SupportJwtTestTokens.Issuer,
                        ValidateAudience = true,
                        ValidAudience = SupportJwtTestTokens.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = SupportJwtTestTokens.Key(),
                    };
                }
            );
        builder.Services.AddTransient<
            IClaimsTransformation,
            NormalizedIdentityClaimsTransformation
        >();
        SupportModule.ConfigureAuthorization(builder.Services);
        app = builder.Build();
        app.UseSupportResponsePolicy();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSupportModule();
        // Keep the actual production MethodInfo/parameter attributes on each delegate. Passing
        // a Delegate local also avoids the route analyzer's cross-assembly method-group crash.
        Delegate create =
            (Func<
                Guid,
                CreateSupportCaseDto,
                HttpContext,
                ISupportCaseService,
                CancellationToken,
                Task<IResult>
            >)
                CreateSupportCaseEndpoint.Put;
        Delegate list =
            (Func<HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                ListSupportCasesEndpoint.Get;
        Delegate detail =
            (Func<Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                GetSupportCaseEndpoint.Get;
        Delegate messages =
            (Func<Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                ListSupportMessagesEndpoint.Get;
        Delegate reply =
            (Func<
                Guid,
                Guid,
                ReplySupportCaseDto,
                HttpContext,
                ISupportCaseService,
                CancellationToken,
                Task<IResult>
            >)
                ReplySupportCaseEndpoint.Put;
        Delegate receipt =
            (Func<Guid, Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                GetSupportReceiptEndpoint.Get;
        Delegate operatorList =
            (Func<HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                OperatorListSupportCasesEndpoint.Get;
        Delegate operatorDetail =
            (Func<Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                OperatorGetSupportCaseEndpoint.Get;
        Delegate operatorMessages =
            (Func<Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                OperatorListSupportMessagesEndpoint.Get;
        Delegate operatorReply =
            (Func<
                Guid,
                Guid,
                ReplySupportCaseDto,
                HttpContext,
                ISupportCaseService,
                CancellationToken,
                Task<IResult>
            >)
                OperatorReplySupportCaseEndpoint.Put;
        Delegate operatorAction =
            (Func<
                Guid,
                Guid,
                ActSupportCaseDto,
                HttpContext,
                ISupportCaseService,
                CancellationToken,
                Task<IResult>
            >)
                ActSupportCaseEndpoint.Put;
        Delegate operatorReceipt =
            (Func<Guid, Guid, HttpContext, ISupportCaseService, CancellationToken, Task<IResult>>)
                OperatorGetSupportReceiptEndpoint.Get;
        app.MapPut("/api/support/cases/{caseId:guid}", create).RequireAuthorization();
        app.MapGet("/api/support/cases", list).RequireAuthorization();
        app.MapGet("/api/support/cases/{caseId:guid}", detail).RequireAuthorization();
        app.MapGet("/api/support/cases/{caseId:guid}/messages", messages).RequireAuthorization();
        app.MapPut("/api/support/cases/{caseId:guid}/messages/{messageId:guid}", reply)
            .RequireAuthorization();
        app.MapGet("/api/support/cases/{caseId:guid}/actions/{actionId:guid}", receipt)
            .RequireAuthorization();
        app.MapGet("/api/support/operator/cases", operatorList)
            .RequireAuthorization(SupportModule.OperatorPolicy);
        app.MapGet("/api/support/operator/cases/{caseId:guid}", operatorDetail)
            .RequireAuthorization(SupportModule.OperatorPolicy);
        app.MapGet("/api/support/operator/cases/{caseId:guid}/messages", operatorMessages)
            .RequireAuthorization(SupportModule.OperatorPolicy);
        app.MapPut(
                "/api/support/operator/cases/{caseId:guid}/messages/{messageId:guid}",
                operatorReply
            )
            .RequireAuthorization(SupportModule.OperatorPolicy);
        app.MapPut(
                "/api/support/operator/cases/{caseId:guid}/actions/{actionId:guid}",
                operatorAction
            )
            .RequireAuthorization(SupportModule.OperatorPolicy);
        app.MapGet(
                "/api/support/operator/cases/{caseId:guid}/actions/{actionId:guid}",
                operatorReceipt
            )
            .RequireAuthorization(SupportModule.OperatorPolicy);

        Delegate classification =
            (Func<
                Guid,
                RequestSupportClassificationDto,
                HttpContext,
                ISupportClassificationService,
                CancellationToken,
                Task<IResult>
            >)
                RequestSupportClassificationEndpoint.Post;
        app.MapPost(
                "/api/support/operator/cases/{caseId:guid}/classification-suggestion",
                classification
            )
            .RequireAuthorization(SupportModule.OperatorPolicy);

        await app.StartAsync();
        Client = app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }
}

public sealed class SupportHttpService : ISupportCaseService, ISupportClassificationService
{
    public static readonly Guid Owner = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static readonly Guid Operator = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public static readonly Guid Case = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid Action = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    public SupportCaller? Caller { get; private set; }
    public object? Command { get; private set; }
    public int Calls { get; private set; }
    public Error? Failure { get; set; }
    public bool Replay { get; set; }

    public Task<ErrorOr<SupportClassificationResult>> SuggestAsync(
        SupportCaller caller,
        RequestSupportClassification request,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Record(
                caller,
                new SupportClassificationResult(
                    request.CaseId,
                    request.RequestId,
                    request.ExpectedRevision,
                    Now,
                    Now.AddMinutes(2),
                    SupportClassificationStatus.Disabled,
                    SupportClassificationReason.Disabled,
                    null,
                    null,
                    null,
                    new(SupportUrgencyKind.Unknown, null, null, null)
                ),
                request
            )
        );

    public void Reset()
    {
        Caller = null;
        Command = null;
        Calls = 0;
        Failure = null;
        Replay = false;
    }

    private ErrorOr<T> Record<T>(SupportCaller caller, T result, object? command = null)
    {
        Calls++;
        Caller = caller;
        Command = command;
        return Failure is { } error ? error
            : caller.Kind == SupportActorKind.Customer && caller.Subject != Owner
                ? SupportErrors.NotFound
            : ErrorOrFactory.From(result);
    }

    private static SupportActionReceipt Receipt(SupportActionKind kind) =>
        new(
            Case,
            Action,
            kind,
            1,
            CaseState.Open,
            Now,
            kind is SupportActionKind.Resolve or SupportActionKind.SetTriage ? null : Action
        );

    public Task<ErrorOr<SupportMutationResult>> CreateAsync(
        SupportCaller caller,
        CreateSupportCase command,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Record(
                caller,
                new SupportMutationResult(Receipt(SupportActionKind.Create), Replay),
                command
            )
        );

    public Task<ErrorOr<SupportMutationResult>> ReplyAsync(
        SupportCaller caller,
        ReplyToSupportCase command,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Record(caller, new SupportMutationResult(Receipt(command.Kind), Replay), command)
        );

    public Task<ErrorOr<SupportMutationResult>> ActAsync(
        SupportCaller caller,
        ActOnSupportCase command,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Record(caller, new SupportMutationResult(Receipt(command.Kind), Replay), command)
        );

    public Task<ErrorOr<SupportCaseView>> GetCaseAsync(
        SupportCaller caller,
        Guid caseId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Record(
                caller,
                new SupportCaseView(
                    Case,
                    Owner,
                    "<script>Fictional private title</script>",
                    CaseState.Open,
                    1,
                    ManualCategory.Unclassified,
                    SupportPriority.Normal,
                    TriageSource.Default,
                    null,
                    null,
                    null,
                    Now,
                    Now,
                    null,
                    null
                )
            )
        );

    public Task<ErrorOr<SupportCasePage>> ListCasesAsync(
        SupportCaller caller,
        SupportCaseFilters? filters = null,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Record(caller, new SupportCasePage([], null)));

    public Task<ErrorOr<SupportMessagePage>> ListMessagesAsync(
        SupportCaller caller,
        Guid caseId,
        string? cursor = null,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Record(
                caller,
                new SupportMessagePage(
                    Case,
                    [new(Case, Action, SupportActorKind.Customer, "Fictional body", 1, Now)],
                    null
                )
            )
        );

    public Task<ErrorOr<SupportActionReceipt>> GetReceiptAsync(
        SupportCaller caller,
        Guid caseId,
        Guid actionId,
        CancellationToken cancellationToken = default
    )
    {
        var result = Record(
            caller,
            Receipt(
                caller.Kind == SupportActorKind.Operator
                    ? SupportActionKind.Resolve
                    : SupportActionKind.Create
            )
        );
        return Task.FromResult(
            caller.Kind == SupportActorKind.Operator && caller.Subject != Operator
                ? (ErrorOr<SupportActionReceipt>)SupportErrors.NotFound
                : result
        );
    }
}
