extern alias TravelHostApp;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Shared.Abstractions;

namespace Travel.Trips.LocalStack;

[TestOnly]
public sealed class FixedE2eTimeProvider(string profile = "moscow") : TimeProvider
{
    public static DateTimeOffset Instant { get; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public static string ProfileInstant(string profile) =>
        profile switch
        {
            "moscow" => "2026-10-24T12:00:00Z",
            "flight-links" => "2026-10-24T12:00:00Z",
            "hotel-links" => "2026-10-24T12:00:00Z",
            "saint-petersburg" => "2026-10-27T12:00:00Z",
            _ => throw new LocalStackRefusal("Unknown E2E city profile."),
        };

    private readonly DateTimeOffset now = DateTimeOffset.Parse(
        ProfileInstant(profile),
        global::System.Globalization.CultureInfo.InvariantCulture
    );

    public override DateTimeOffset GetUtcNow() => now;
}

// Executes the actual Host entry point with the local fixture clock and loopback Keycloak.
[TestOnly]
public sealed class E2eHostFactory(string profile = "moscow", SavedOptionBarrier? barrier = null)
    : WebApplicationFactory<TravelHostApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentFor(profile));
        builder.UseContentRoot("/host");
        builder.ConfigureServices(services =>
        {
            ConfigureClocks(services, profile);
            if (profile is "hotel-links" or "spb-option" or "saved-spb-option")
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        if (
                            options.Authority
                            != $"http://127.0.0.1:{(profile == "saved-spb-option" ? 8187 : 8186)}/realms/travel"
                        )
                            throw new LocalStackRefusal(
                                "Testing fixtures require the owned loopback Keycloak."
                            );
                        options.RequireHttpsMetadata = false;
                    }
                );
            if (profile == "hotel-links")
                HotelLinksSeed.Configure(services);
            if (profile is "spb-option" or "saved-spb-option")
                services.AddSingleton<IStartupFilter, OptionHttpObservation>();
            if (barrier is not null)
            {
                services.ConfigureDbContext<TripsDbContext>(options =>
                    options.AddInterceptors(barrier)
                );
                if (Environment.GetEnvironmentVariable("TRIPS_FIXTURE_EXPIRED_SOURCE") == "true")
                {
                    services.RemoveAll<TripOptionScenario>();
                    services.AddSingleton(
                        new TripOptionScenario(
                            "B",
                            new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero)
                        )
                    );
                }
            }
        });
    }

    public static void ConfigureClocks(IServiceCollection services, string profile = "moscow")
    {
        if (profile is not ("spb-option" or "saved-spb-option"))
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedE2eTimeProvider(profile));
        }
        services.PostConfigureAll<JwtBearerOptions>(options =>
            options.TimeProvider = TimeProvider.System
        );
    }

    private static string EnvironmentFor(string? profile) =>
        profile is "hotel-links" or "spb-option" or "saved-spb-option"
            ? "Testing"
            : Environments.Development;

    public static string? ApplicationInstant(string profile) =>
        profile is "spb-option" or "saved-spb-option"
            ? null
            : FixedE2eTimeProvider.ProfileInstant(profile);

    public static void ValidateMode(string mode, string environment, string? profile = null)
    {
        if (
            mode != (profile == "saved-spb-option" ? "saved-spb-option" : "e2e")
            || environment != EnvironmentFor(profile)
        )
            throw new LocalStackRefusal(
                "Fixed application time requires the matching owned E2E container environment."
            );
    }

    public static void ValidateListeners(IEnumerable<string> addresses)
    {
        var listeners = addresses.Select(address => new Uri(address)).ToArray();
        if (
            listeners.Length != 2
            || listeners.Any(uri => uri.Scheme != "http")
            || !listeners.Any(uri => uri.Port == 5099)
            || !listeners.Any(uri => uri.Port == 5098 && uri.IsLoopback)
        )
            throw new LocalStackRefusal(
                "The real Host must expose the owned API and loopback health listeners."
            );
    }

    public static async Task ServeAsync(
        LocalStackState state,
        string hostPath,
        CancellationToken ct
    )
    {
        var profile = state.Mode == "saved-spb-option" ? state.Mode : state.E2eProfile ?? "moscow";
        ValidateMode(
            state.Mode,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "",
            profile
        );
        if (
            hostPath != "/host"
            || !File.Exists("/host/Travel.Host.dll")
            || !File.Exists("/host/appsettings.json")
        )
            throw new LocalStackRefusal("The published Host mount is required.");
        LocalStackState.RequireNoLinks(hostPath);
        LocalDatabase.ValidateConnection(
            Environment.GetEnvironmentVariable("ConnectionStrings__travel") ?? "",
            LocalProvisioning.ReadSecrets(state).DatabasePassword
        );
        // MVC Testing checks this setting before ConfigureWebHost and before its solution-directory fallback.
        Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_TRAVEL_HOST", hostPath);
        if (state.ApplicationAsOfUtc != ApplicationInstant(profile))
            throw new LocalStackRefusal("E2E state clock does not match the declared profile.");
        var barrier = state.E2eProfile == "saved-spb-option" ? new SavedOptionBarrier() : null;
        await using var factory = new E2eHostFactory(profile, barrier);
        factory.UseKestrel();
        factory.StartServer();
        ValidateListeners(
            factory
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses
                ?? []
        );
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            lifetime.ApplicationStopping
        );
        try
        {
            if (barrier is not null)
                await barrier.ServeAsync(state.Id, linked.Token);
            else if (FlightLinksSeed.SupportsProfile(state.E2eProfile))
                await FlightLinksSeed.ServeAsync(state, factory.Services, linked.Token);
            else
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }
}
