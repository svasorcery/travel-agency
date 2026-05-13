using Alba;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Host.Features.Status;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "Integration")]
public class StatusEndpointTests : IntegrationTestBase
{
    [Fact]
    public async Task Get_status_returns_200_with_postgres_version_and_db_ok()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-06-15T10:00:00Z"));

        await using var host = await AlbaHost.For<Program>(builder =>
        {
            // Inject the Testcontainers connection string BEFORE Program.cs runs AddNpgsqlDbContext.
            // The Aspire client integration resolves ConnectionStrings:travel from IConfiguration —
            // overriding it here makes the EF DbContext point at our test container.
            builder.UseSetting("ConnectionStrings:travel", ConnectionString);

            // Disable Aspire's OTel exporter in tests (no OTLP endpoint running).
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");

            builder.ConfigureServices(services =>
            {
                // Replace the system TimeProvider with a fake so we can assert exact timestamps.
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(fakeTime);
            });
        });

        var result = await host.Scenario(_ =>
        {
            _.Get.Url("/api/status");
            _.StatusCodeShouldBeOk();
            _.ContentTypeShouldBe("application/json; charset=utf-8");
        });

        var response = result.ReadAsJson<StatusResponse>();
        response.ShouldNotBeNull();
        response!.Db.ShouldBe("ok");
        response.Version.ShouldNotBeNullOrEmpty();
        response.Timestamp.ShouldBe(fakeTime.GetUtcNow());
    }
}
