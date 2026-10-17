using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelDisabledTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Default_off_initializer_never_requires_database_or_schema(string environment)
    {
        // Port1 has no database: any migration/schema query makes this check fail.
        await using var db = new HotelsDbContext(
            new DbContextOptionsBuilder<HotelsDbContext>()
                .UseNpgsql(
                    "Host=127.0.0.1;Port=1;Database=fictional_unavailable;Username=fictional;Timeout=1"
                )
                .Options
        );
        var initializer = new HotelsEfInitializer(
            db,
            new EnvironmentStub { EnvironmentName = environment },
            Options.Create(new HotelsOptions()),
            new HotelsSchemaReadiness()
        );
        await initializer.InitializeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Missing_private_keys_fail_closed_without_automatic_key_or_directory_bootstrap()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "travel-hotels-uninitialized-" + Guid.NewGuid().ToString("N")
        );
        using var keys = new HotelPiiProtectionProvider(
            Options.Create(
                new HotelPiiProtectionOptions
                {
                    KeyRingPath = Path.Combine(root, "ring"),
                    ActiveCertificatePath = Path.Combine(root, "fictional.pfx"),
                }
            ),
            TimeProvider.System
        );
        keys.Availability.ShouldBe(HotelProtectionAvailability.Unavailable);
        keys.Protect([1, 2], "fictional-purpose").IsError.ShouldBeTrue();
        Directory.Exists(root).ShouldBeFalse();
    }

    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Fictional Hotels test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
