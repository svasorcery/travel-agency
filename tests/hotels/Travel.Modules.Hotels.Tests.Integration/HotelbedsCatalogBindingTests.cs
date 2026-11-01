using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Api.Composition;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelbedsCatalogBindingTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(100)]
    public void Actual_module_binding_replaces_default_with_exact_configured_catalog(int count)
    {
        var ids = Enumerable.Range(900001, count).ToArray();
        using var services = Services(ids, true);
        services
            .GetRequiredService<IOptions<HotelbedsOptions>>()
            .Value.FictionalHotelIds.ShouldBe(ids);
    }

    [Fact]
    public void Unspecified_catalog_preserves_the_default()
    {
        using var services = Services(null, false);
        services
            .GetRequiredService<IOptions<HotelbedsOptions>>()
            .Value.FictionalHotelIds.ShouldBe([900001]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_empty_or_null_catalog_is_not_replaced_with_default(bool empty)
    {
        using var services = Services(empty ? [] : null, true);
        Should.Throw<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<HotelbedsOptions>>().Value
        );
    }

    private static ServiceProvider Services(int[]? ids, bool configured)
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = "Testing",
            }
        );
        var values = new Dictionary<string, object?>
        {
            ["Enabled"] = true,
            ["Fictional"] = true,
            ["AccountAlias"] = "owned-catalog",
            ["ConfigurationRevision"] = "catalog-v1",
            ["ApprovalRevision"] = "catalog-v1",
            ["ApiKey"] = "owned-fictional-key",
            ["Secret"] = "owned-fictional-secret",
        };
        if (configured)
            values["FictionalHotelIds"] = ids;
        var json = JsonSerializer.Serialize(new { Hotels = new { Hotelbeds = values } });
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        builder.AddHotelsModule();
        // Resolving options alone never builds/starts Host, a DbContext, transport or initializer.
        return builder.Services.BuildServiceProvider();
    }
}
