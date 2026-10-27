using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    public void Disabled_registration_only_adds_feature_without_database_or_keys()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { { "Trips:Enabled", "false" } }
        );
        var count = builder.Services.Count;
        builder.AddTripsModule();
        builder.Services.Count.ShouldBe(count + 3);
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
