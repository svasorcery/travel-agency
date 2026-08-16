using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

public sealed class FlightsDbContextConfigurationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=travel;Username=postgres;Password=postgres";

    [Fact]
    public void Runtime_and_design_time_options_use_the_same_provider_metadata()
    {
        using var runtimeContext = new FlightsDbContext(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(ConnectionString, FlightsDbContextConfiguration.ConfigureNpgsql)
                .UseSnakeCaseNamingConvention()
                .Options
        );
        using var designTimeContext = CreateDesignTimeContext();

        var runtimeMetadata = CaptureProviderMetadata(runtimeContext);
        var designTimeMetadata = CaptureProviderMetadata(designTimeContext);

        AssertRequiredMetadata(runtimeMetadata);
        AssertRequiredMetadata(designTimeMetadata);
        runtimeMetadata.ShouldBe(designTimeMetadata);
    }

    private static FlightsDbContext CreateDesignTimeContext()
    {
        var factoryType = typeof(FlightsDbContext).Assembly.GetType(
            "Travel.Modules.Flights.Infrastructure.Persistence.FlightsDbContextFactory",
            throwOnError: true
        )!;
        var factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        var createDbContext = factoryType.GetMethod("CreateDbContext")!;

        return (FlightsDbContext)createDbContext.Invoke(factory, [Array.Empty<string>()])!;
    }

    private static ProviderMetadata CaptureProviderMetadata(FlightsDbContext context)
    {
        var table = StoreObjectIdentifier.Table("idempotency_keys", "flights");
        var responseStatusColumn = context
            .Model.FindEntityType(typeof(IdempotencyKeyEntity))!
            .FindProperty(nameof(IdempotencyKeyEntity.ResponseStatus))!
            .GetColumnName(table)!;

        return new ProviderMetadata(
            context.Database.ProviderName!,
            context.Model.GetDefaultSchema()!,
            responseStatusColumn,
            context.GetService<IMigrationsAssembly>().Assembly.GetName().Name!,
            context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript()
        );
    }

    private static void AssertRequiredMetadata(ProviderMetadata metadata)
    {
        metadata.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
        metadata.DefaultSchema.ShouldBe("flights");
        metadata.ResponseStatusColumn.ShouldBe("response_status");
        metadata.MigrationsAssembly.ShouldBe("Travel.Modules.Flights.Infrastructure");
        metadata.MigrationsHistoryCreateScript.ShouldContain(
            "CREATE TABLE IF NOT EXISTS flights.__ef_migrations_history"
        );
    }

    private sealed record ProviderMetadata(
        string ProviderName,
        string DefaultSchema,
        string ResponseStatusColumn,
        string MigrationsAssembly,
        string MigrationsHistoryCreateScript
    );
}

[Trait("Category", "Integration")]
public sealed class FlightsDbContextTests : IntegrationTestBase
{
    private FlightsDbContext _db = default!;

    protected override async ValueTask OnInitializedAsync()
    {
        var opts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(opts);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Can_insert_and_read_IdempotencyKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        _db.IdempotencyKeys.Add(
            new IdempotencyKeyEntity
            {
                Key = "abc",
                UserId = Guid.NewGuid(),
                Route = "/test",
                BodyHash = "h",
                ResponseStatus = 200,
                CreatedAt = now,
                ExpiresAt = now.AddHours(24),
            }
        );
        await _db.SaveChangesAsync(ct);

        var found = await _db.IdempotencyKeys.FindAsync(["abc"], ct);
        found.ShouldNotBeNull();
        found.Route.ShouldBe("/test");
    }

    [Fact]
    public async Task Can_insert_and_read_WebhookInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var entity = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = "duffel",
            EventId = "evt_001",
            EventType = "order.created",
            RawPayload = """{"foo":"bar"}""",
            Signature = "sig123",
            ReceivedAt = now,
        };
        _db.WebhookInbox.Add(entity);
        await _db.SaveChangesAsync(ct);

        var found = await _db.WebhookInbox.FindAsync([entity.Id], ct);
        found.ShouldNotBeNull();
        found.EventType.ShouldBe("order.created");
    }

    [Fact]
    public async Task Can_insert_and_read_DeeplinkOfferCache()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var entity = new DeeplinkOfferCacheEntity
        {
            Id = Guid.NewGuid(),
            CriteriaHash = "hash_abc",
            OffersJson = """[{"id":"offer1"}]""",
            FetchedAt = now,
            ExpiresAt = now.AddMinutes(30),
        };
        _db.DeeplinkOffersCache.Add(entity);
        await _db.SaveChangesAsync(ct);

        var found = await _db.DeeplinkOffersCache.FindAsync([entity.Id], ct);
        found.ShouldNotBeNull();
        found.CriteriaHash.ShouldBe("hash_abc");
    }

    [Fact]
    public async Task Can_insert_and_read_OrderReadModel()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var entity = new OrderReadModelEntity
        {
            Id = Guid.NewGuid(),
            AggregateId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = "confirmed",
            TotalAmount = 299.99m,
            Currency = "USD",
            ItineraryJson = """{"segments":[]}""",
            PassengerInfoJson = """{"passengers":[]}""",
            TicketNumbers = ["TK001", "TK002"],
            BookedAt = now,
        };
        _db.Orders.Add(entity);
        await _db.SaveChangesAsync(ct);

        var found = await _db.Orders.FindAsync([entity.Id], ct);
        found.ShouldNotBeNull();
        found.Status.ShouldBe("confirmed");
        found.TicketNumbers.ShouldBe(["TK001", "TK002"]);
    }
}
