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
using Travel.Modules.Trips.Application.HotelLinks;
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
            typeof(TripOptionFeature),
            typeof(TripFlightLinksFeature),
            typeof(TripHotelLinksFeature),
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

    [Theory]
    [InlineData("Testing", true, true)]
    [InlineData("Testing", false, false)]
    [InlineData("Development", true, false)]
    [InlineData("Production", true, false)]
    public void Hotel_links_register_dependencies_only_in_enabled_Testing(
        string environment,
        bool flag,
        bool enabled
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environment }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = "true",
                ["Trips:HotelLinks:Enabled"] = flag.ToString(),
            }
        );
        builder.AddTripsModule();
        builder
            .Services.Single(d => d.ServiceType == typeof(TripHotelLinksFeature))
            .ImplementationInstance.ShouldBe(new TripHotelLinksFeature(enabled));
        builder.Services.Any(d => d.ServiceType == typeof(TripHotelLinkService)).ShouldBe(enabled);
        builder.Services.Any(d => d.ServiceType == typeof(ITripHotelLinkStore)).ShouldBe(enabled);
    }

    [Fact]
    public void Private_hotel_link_paths_are_filtered_before_AspNetCore_tracing()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Trips:Enabled"] = "false" }
        );
        builder.AddTripsModule();
        using var provider = builder.Services.BuildServiceProvider();
        var filter = provider
            .GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>()
            .Value.Filter!;
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/trips/hotel-link/availability";
        filter(context).ShouldBeFalse();
        context.Request.Path = $"/api/trips/{Guid.NewGuid():D}/hotel-link/status";
        filter(context).ShouldBeFalse();
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
