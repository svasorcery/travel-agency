using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Hotels.Api.Composition;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Http;
using Wolverine.Postgresql;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelStoreFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    public IHost Host { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string Connection => postgres.GetConnectionString();
    public ControlledProvider Provider { get; } = new();
    public ControlledCapacity Capacity { get; } = new();
    public ControlledProtection Protection { get; } = new();
    private readonly string keyDirectory = Path.Combine(
        Path.GetTempPath(),
        "travel-hotels-tests-" + Guid.NewGuid().ToString("N")
    );

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(keyDirectory);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=fictional-hotels-tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(3)
        );
        var certificate = Path.Combine(keyDirectory, "fictional.pfx");
        await File.WriteAllBytesAsync(
            certificate,
            cert.Export(X509ContentType.Pfx),
            TestContext.Current.CancellationToken
        );
        var options = new HotelPiiProtectionOptions
        {
            KeyRingPath = Path.Combine(keyDirectory, "ring"),
            ActiveCertificatePath = certificate,
        };
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ApplicationName = typeof(HotelStoreFixture).Assembly.FullName,
            }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:travel"] = Connection,
                ["Hotels:Enabled"] = "true",
                ["Hotels:BookingEnabled"] = "true",
                ["Hotels:ReceiverEnabled"] = "true",
                ["Hotels:ApiKey"] = "duffel_test_fictional_only",
                ["Hotels:WebhookSecret"] = "fictional-webhook-secret",
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddLogging();
        builder.AddHotelsModule();
        builder.Services.RemoveAll<IHotelSearchProvider>();
        builder.Services.RemoveAll<IHotelBookingProvider>();
        builder.Services.RemoveAll<IHotelCapacity>();
        builder.Services.RemoveAll<HotelPiiProtectionProvider>();
        builder.Services.RemoveAll<IOptions<HotelPiiProtectionOptions>>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IOptions<HotelPiiProtectionOptions>>(Options.Create(options));
        builder.Services.AddSingleton<HotelPiiProtectionProvider>();
        builder.Services.AddSingleton<IHotelPrivateProtector>(s =>
        {
            Protection.Inner = new HotelPrivateProtector(
                s.GetRequiredService<HotelPiiProtectionProvider>()
            );
            return Protection;
        });
        builder.Services.AddSingleton<IHotelBookingProvider>(Provider);
        builder.Services.AddSingleton<IHotelSearchProvider>(Provider);
        builder.Services.AddSingleton<IHotelCapacity>(Capacity);
        builder.Services.AddScoped<IHotelReservationStore, HotelReservationStore>();
        builder.Services.AddScoped<HotelReservationService>();
        builder
            .Services.AddAuthentication("FictionalHotels")
            .AddScheme<AuthenticationSchemeOptions, FictionalHotelsAuthHandler>(
                "FictionalHotels",
                _ => { }
            );
        builder.Services.AddAuthorization(o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
        );
        builder.Host.UseWolverine(o =>
        {
            o.ApplicationAssembly = typeof(HotelsModule).Assembly;
            o.PersistMessagesWithPostgresql(Connection);
            o.UseEntityFrameworkCoreTransactions();
            o.Policies.UseDurableLocalQueues();
            o.Policies.AutoApplyTransactions();
            HotelsModule.ConfigureWolverine(o);
        });
        var app = builder.Build();
        Host = app;
        app.UseHotelsResponsePolicy();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseHotelsModule();
        app.MapWolverineEndpoints();
        using (var scope = Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
        Host.Services.GetRequiredService<HotelPiiProtectionProvider>()
            .Initialize()
            .IsError.ShouldBeFalse();
        await Host.StartAsync(TestContext.Current.CancellationToken);
        Client = app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync(TestContext.Current.CancellationToken);
            Host.Dispose();
        }
        await postgres.DisposeAsync();
        var resolved = Path.GetFullPath(keyDirectory);
        if (
            !resolved.StartsWith(
                Path.GetFullPath(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase
            )
            || !Path.GetFileName(resolved)
                .StartsWith("travel-hotels-tests-", StringComparison.Ordinal)
        )
            throw new InvalidOperationException();
        Directory.Delete(resolved, recursive: true);
    }
}

public sealed class ControlledProtection : IHotelPrivateProtector
{
    public IHotelPrivateProtector Inner { get; set; } = null!;
    public bool Offline { get; set; }
    public int Protects { get; private set; }
    public int Decrypts { get; private set; }

    public ErrorOr.ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding)
    {
        Protects++;
        return Offline ? HotelErrors.Unavailable : Inner.Protect(plaintext, binding);
    }

    public ErrorOr.ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding)
    {
        Decrypts++;
        return Offline ? HotelErrors.Unavailable : Inner.Unprotect(envelope, binding);
    }
}

public sealed class ControlledHotelWorkHandler
{
    public void Handle(HotelWork work) { }
}

public sealed class ControlledCapacity : IHotelCapacity
{
    private int active;
    public bool Available { get; set; } = true;
    public int Active => active;

    public IDisposable? TryAcquire(string provider, bool recovery = false)
    {
        if (!Available)
            return null;
        Interlocked.Increment(ref active);
        return new Permit(() => Interlocked.Decrement(ref active));
    }

    private sealed class Permit(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

public sealed class ControlledProvider
    : IHotelBookingProvider,
        IHotelSearchProvider,
        IHotelLegacyContextConsumer
{
    private int creates;
    public int Creates => creates;
    public string Key => "semantic-alternative";

    public bool AcceptsLegacyContext(ProviderContextHandle context) =>
        context.Version == 1 && context.Value == "fictional-context-without-source-quote";

    public CapabilitySupport GetSupport(SelectedStay selection) =>
        selection.Request.Occupancy.Adults is 1 or 2
            ? CapabilitySupport.Yes
            : CapabilitySupport.No("AdultsUnsupported");

    public CapabilitySupport GetSupport(StaySearch request) => CapabilitySupport.Yes;

    public Task<HotelExecution<HotelOffer>> SearchAsync(
        StaySearch request,
        CancellationToken cancellationToken
    )
    {
        var terms = DuffelStaysProviderTests.Terms() with
        {
            Dates = request.Dates,
            Occupancy = request.Occupancy,
        };
        return Task.FromResult(
            new HotelExecution<HotelOffer>(
                ExecutionKind.Complete,
                [
                    new(
                        Key,
                        "Test",
                        "fictional_rate",
                        terms,
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        true,
                        null
                    ),
                ]
            )
        );
    }

    public Task<HotelExecution<HotelOffer>> LoadRatesAsync(
        SelectedStay selection,
        CancellationToken cancellationToken
    ) => SearchAsync(selection.Request, cancellationToken);

    public Task<Preparation> PrepareAsync(
        SelectedStay selection,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new Preparation(
                PreparationKind.Ready,
                new(
                    selection.Offer.Terms,
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    new(1, "fictional-context-without-source-quote")
                )
            )
        );

    public Task<BookingObservation> CreateAsync(
        CreateStay purchase,
        CancellationToken cancellationToken
    )
    {
        Interlocked.Increment(ref creates);
        return Task.FromResult(
            new BookingObservation(
                ObservationKind.Confirmed,
                new("bok_" + purchase.AttemptId.ToString("N"), null, purchase.Accepted)
            )
        );
    }
}

public sealed class FictionalHotelsAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Fictional-Owner", out var owner))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("sub", owner.ToString()), new("aud", "travel-web") };
        if (Request.Headers["X-Fictional-Scope"] != "none")
            claims.Add(new("scope", "hotels:book"));
        return Task.FromResult(
            AuthenticateResult.Success(
                new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)
            )
        );
    }
}
