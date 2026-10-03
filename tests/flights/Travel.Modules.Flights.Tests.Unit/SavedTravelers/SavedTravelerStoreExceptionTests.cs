using System.Data;
using System.Data.Common;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;

namespace Travel.Modules.Flights.Tests.Unit.SavedTravelers;

public sealed class SavedTravelerStoreExceptionTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly SavedTravelerId Id = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string, string> OperationsAndTransientFailures
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var operation in new[] { "get", "list", "visibility", "update", "delete" })
            foreach (
                var failure in new[] { "npgsql", "timeout", "update-npgsql", "update-timeout" }
            )
                data.Add(operation, failure);
            return data;
        }
    }

    [Theory]
    [InlineData("npgsql")]
    [InlineData("timeout")]
    [InlineData("update-npgsql")]
    [InlineData("update-timeout")]
    public async Task Actual_nonretrying_provider_wraps_transient_failure_before_connection_open(
        string kind
    )
    {
        var failure = Failure(kind);
        var interceptor = new ThrowBeforeOpen(failure);
        await using var db = Context(interceptor);
        var strategy = db.Database.CreateExecutionStrategy();
        strategy.RetriesOnFailure.ShouldBeFalse();
        strategy
            .GetType()
            .FullName.ShouldBe(
                "Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.NpgsqlExecutionStrategy"
            );
        // Actual query pipeline and installed provider strategy, not a synthesized wrapper.
        var wrapped = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await db.SavedTravelers.CountAsync(TestContext.Current.CancellationToken)
        );
        wrapped.InnerException.ShouldBeSameAs(failure);
        AssertNeverOpened(db, interceptor);
    }

    [Theory]
    [MemberData(nameof(OperationsAndTransientFailures))]
    public async Task Wrapped_transient_store_failure_is_fixed_safe_503_once(
        string operation,
        string kind
    )
    {
        var interceptor = new ThrowBeforeOpen(Failure(kind));
        await using var db = Context(interceptor);
        var store = new SavedTravelerStore(db);
        var ct = TestContext.Current.CancellationToken;
        switch (operation)
        {
            case "get":
                Safe(await store.GetOwnedAsync(Owner, Id, ct));
                break;
            case "list":
                Safe(await store.ListOwnedAsync(Owner, 0, ct));
                break;
            case "visibility":
                Safe(await store.GetVisibilityAsync(Owner, Id, ct));
                break;
            case "update":
                Safe(await store.UpdateAsync(Record(), Guid.NewGuid(), ct));
                break;
            case "delete":
                Safe(await store.DeleteAsync(Owner, Id, Guid.NewGuid(), ct));
                break;
            default:
                throw new InvalidOperationException("Unknown test operation.");
        }
        AssertNeverOpened(db, interceptor);
    }

    [Theory]
    [InlineData("npgsql")]
    [InlineData("timeout")]
    [InlineData("update-npgsql")]
    [InlineData("update-timeout")]
    public async Task Wrapped_create_failure_detaches_only_attempted_insert_and_returns_safe_503(
        string kind
    )
    {
        var interceptor = new ThrowBeforeOpen(Failure(kind));
        await using var db = Context(interceptor);
        var unrelated = new SavedTravelerEntity
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Owner,
            Revision = Guid.NewGuid(),
        };
        db.SavedTravelers.Attach(unrelated);
        Safe(
            await new SavedTravelerStore(db).CreateAsync(
                Record(),
                TestContext.Current.CancellationToken
            )
        );
        db.ChangeTracker.Entries<SavedTravelerEntity>().Select(x => x.Entity).ShouldBe([unrelated]);
        db.Entry(unrelated).State.ShouldBe(EntityState.Unchanged);
        AssertNeverOpened(db, interceptor);
    }

    [Fact]
    public async Task Direct_nontransient_database_exception_still_returns_safe_storage_failure()
    {
        var failure = new NpgsqlException("fictional-error-sentinel");
        failure.IsTransient.ShouldBeFalse();
        var interceptor = new ThrowBeforeOpen(failure);
        await using var db = Context(interceptor);
        Safe(
            await new SavedTravelerStore(db).GetVisibilityAsync(
                Owner,
                Id,
                TestContext.Current.CancellationToken
            )
        );
        AssertNeverOpened(db, interceptor);
    }

    [Theory]
    [InlineData("programming")]
    [InlineData("nested-programming")]
    [InlineData("nontransient-wrapper")]
    [InlineData("cancellation")]
    public async Task Programming_failures_and_cancellation_propagate_and_failed_create_is_detached(
        string kind
    )
    {
        Exception failure = kind switch
        {
            "programming" => new InvalidOperationException("fictional-programming-sentinel"),
            "nested-programming" => new InvalidOperationException(
                "fictional-programming-sentinel",
                new InvalidOperationException("inner", new TimeoutException("unrelated"))
            ),
            "nontransient-wrapper" => new InvalidOperationException(
                "fictional-programming-sentinel",
                new NpgsqlException("nontransient")
            ),
            _ => new OperationCanceledException(TestContext.Current.CancellationToken),
        };
        var interceptor = new ThrowBeforeOpen(failure);
        await using var db = Context(interceptor);
        var thrown = await Xunit.Record.ExceptionAsync(async () =>
            await new SavedTravelerStore(db).CreateAsync(
                Record(),
                TestContext.Current.CancellationToken
            )
        );
        thrown.ShouldBeSameAs(failure);
        db.ChangeTracker.Entries<SavedTravelerEntity>().ShouldBeEmpty();
        AssertNeverOpened(db, interceptor);
    }

    private static Exception Failure(string kind)
    {
        var timeout = new TimeoutException("fictional-error-sentinel");
        var npgsql = new NpgsqlException(
            "fictional-error-sentinel",
            new IOException("fictional-transport-sentinel")
        );
        npgsql.IsTransient.ShouldBeTrue();
        return kind switch
        {
            "npgsql" => npgsql,
            "timeout" => timeout,
            "update-npgsql" => new DbUpdateException("fictional-update-sentinel", npgsql),
            "update-timeout" => new DbUpdateException("fictional-update-sentinel", timeout),
            _ => throw new InvalidOperationException("Unknown test failure."),
        };
    }

    private static FlightsDbContext Context(ThrowBeforeOpen interceptor) =>
        new(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(
                    "Host=fictional.invalid;Database=never_opened;Username=fictional;Password=fictional"
                )
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(interceptor)
                .Options
        );

    private static SavedTravelerStoredRecord Record() =>
        new(
            Id,
            Owner,
            Guid.NewGuid(),
            ProtectedSavedTravelerSnapshot.Create(1, "Y2lwaGVydGV4dA==").Value,
            Now,
            Now
        );

    private static void Safe<T>(ErrorOr<T> result)
    {
        result.IsError.ShouldBeTrue();
        result.Errors.Count.ShouldBe(1);
        result.FirstError.Code.ShouldBe("Flights.TravelerStorageUnavailable");
        result.FirstError.Type.ShouldBe((ErrorType)503);
        result.FirstError.Description.ShouldBe("Saved traveler storage is unavailable.");
        result.FirstError.Metadata.ShouldBeNull();
        result.FirstError.ToString().ShouldNotContain("sentinel");
    }

    private static void AssertNeverOpened(FlightsDbContext db, ThrowBeforeOpen interceptor)
    {
        interceptor.Attempts.ShouldBe(1);
        interceptor.Opened.ShouldBe(0);
        db.Database.GetDbConnection().State.ShouldBe(ConnectionState.Closed);
    }

    private sealed class ThrowBeforeOpen(Exception failure) : DbConnectionInterceptor
    {
        public int Attempts;
        public int Opened;

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            Attempts++;
            connection.State.ShouldBe(ConnectionState.Closed);
            // Throw synchronously before Npgsql Open/OpenAsync: no DNS/socket/database access.
            throw failure;
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Opened++;
            throw new InvalidOperationException("Test unexpectedly opened a connection.");
        }
    }
}
