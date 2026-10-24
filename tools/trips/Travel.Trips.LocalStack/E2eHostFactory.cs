extern alias TravelHostApp;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Travel.Shared.Abstractions;

namespace Travel.Trips.LocalStack;

[TestOnly]
public sealed class FixedE2eTimeProvider : TimeProvider
{
    public static DateTimeOffset Instant { get; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Instant;
}

// Executes the actual Host entry point. No replacement auth handler, transport, store, or application configuration.
[TestOnly]
public sealed class E2eHostFactory : WebApplicationFactory<TravelHostApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseContentRoot("/host");
        builder.ConfigureServices(ConfigureClocks);
    }

    public static void ConfigureClocks(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider, FixedE2eTimeProvider>();
        services.PostConfigureAll<JwtBearerOptions>(options =>
            options.TimeProvider = TimeProvider.System
        );
    }

    public static void ValidateMode(string mode, string environment)
    {
        if (mode != "e2e" || environment != Environments.Development)
            throw new LocalStackRefusal(
                "Fixed application time requires the E2E Development container."
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
        ValidateMode(
            state.Mode,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? ""
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
        await using var factory = new E2eHostFactory();
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
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
    }
}
