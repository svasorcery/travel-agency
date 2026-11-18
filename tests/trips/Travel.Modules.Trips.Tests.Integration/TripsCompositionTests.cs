using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;
using Shouldly;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsCompositionTests
{
    [Fact]
    public void Disabled_registration_only_installs_flags_and_private_request_diagnostics()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { { "Trips:Enabled", "false" } }
        );
        var before = builder.Services.ToArray();
        builder.AddTripsModule();
        var added = builder.Services.Except(before).Select(d => d.ServiceType).ToArray();
        Type[] permitted =
        [
            typeof(TripsFeature),
            typeof(TripFlightLinksFeature),
            typeof(TripsBriefExtractionFeature),
            typeof(TripsItineraryFeature),
            typeof(ILoggerFactory),
            typeof(IHttpContextAccessor),
            typeof(IConfigureOptions<AspNetCoreTraceInstrumentationOptions>),
        ];
        added.ShouldBe(permitted, ignoreOrder: true);
        before
            .Except(builder.Services)
            .Select(d => d.ServiceType)
            .ShouldBe([typeof(ILoggerFactory)]);
        builder.Services.ShouldNotContain(value =>
            value.ServiceType == typeof(TripsDbContext)
            || value.ServiceType == typeof(TripsProtectionProvider)
            || value.ServiceType == typeof(TripPlanService)
            || value.ServiceType == typeof(ITripsReadiness)
        );
    }

    [Fact]
    public async Task Missing_protection_fails_before_database_resolution()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddScoped<TripsDbContext>(_ =>
        {
            calls++;
            throw new InvalidOperationException("Database must stay unresolved.");
        });
        using var container = services.BuildServiceProvider();
        using var protection = new TripsProtectionProvider(
            Options.Create(new TripsProtectionOptions()),
            TimeProvider.System
        );
        var readiness = new TripsReadiness(
            container.GetRequiredService<IServiceScopeFactory>(),
            protection
        );
        (await readiness.IsReadyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        calls.ShouldBe(0);
    }
}
