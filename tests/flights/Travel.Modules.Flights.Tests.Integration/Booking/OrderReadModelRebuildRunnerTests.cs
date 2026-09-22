using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

public sealed class OrderReadModelRebuildRunnerTests : IClassFixture<BookingReconcilerFixture>
{
    private readonly BookingReconcilerFixture _fixture;

    public OrderReadModelRebuildRunnerTests(BookingReconcilerFixture fixture) => _fixture = fixture;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Catalog_crosses_page_boundary_without_repeats()
    {
        var expected = new HashSet<Guid>();
        for (var i = 0; i < 130; i++)
            expected.Add(await _fixture.SeedAsync(null));
        var actual = new List<Guid>();
        await foreach (var id in new BookingStreamCatalog(_fixture.Store).ReadIdsAsync(Ct))
            actual.Add(id);
        actual.Distinct().Count().ShouldBe(actual.Count);
        expected.IsSubsetOf(actual).ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_retains_synchronously_written_progress_for_committed_streams()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var output = new StringWriter();
        using var db = new FlightsDbContext(_fixture.Options);
        var services = new ServiceCollection();
        services.AddSingleton<global::Marten.IDocumentStore>(_fixture.Store);
        services.AddBookingReadModelMaintenance(db.Database.GetConnectionString()!, true, output);
        services.AddScoped<IBookingStreamCatalog>(_ => new CancellingCatalog(id, cancellation));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            scope
                .ServiceProvider.GetRequiredService<IOrderReadModelRebuildRunner>()
                .RunAsync(true, true, cancellation.Token)
        );
        var progress = System.Text.Json.JsonSerializer.Deserialize<BookingMaintenanceProgress>(
            output.ToString().Trim()
        );
        progress.ShouldNotBeNull();
        progress.Succeeded.ShouldBe(1);
        progress.Stream.AggregateId.ShouldBe(id);
    }

    private sealed class CancellingCatalog(Guid id, CancellationTokenSource cancellation)
        : IBookingStreamCatalog
    {
        public async IAsyncEnumerable<Guid> ReadIdsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            yield return id;
            await Task.Yield();
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
        }
    }

    [Fact]
    public async Task Catalog_failure_preserves_completed_stream_report_and_counts()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        using var provider = Maintenance();
        using var scope = provider.CreateScope();
        var runner = new OrderReadModelRebuildRunner(
            new FailingCatalog(id),
            scope.ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
        );
        var report = await runner.RunAsync(true, true, Ct);
        report.Succeeded.ShouldBe(1);
        report.Streams.Single().AggregateId.ShouldBe(id);
        report.FailureCode.ShouldBe("CatalogReadFailed");
    }

    private sealed class FailingCatalog(Guid id) : IBookingStreamCatalog
    {
        public async IAsyncEnumerable<Guid> ReadIdsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            yield return id;
            await Task.Yield();
            throw new InvalidOperationException("test-only enumeration failure");
        }
    }

    [Fact]
    public async Task Validation_compares_timestamps_at_PostgreSQL_storage_precision()
    {
        var id = await _fixture.SeedAsync(null);
        var at = BookingReconcilerFixture.Now.AddTicks(17);
        await _fixture.AppendAsync(
            id,
            BookingReconcilerFixture.Held(Guid.NewGuid()) with
            {
                HeldAt = at,
            }
        );
        using var provider = Maintenance();
        using var scope = provider.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOrderReadModelReconciler>();
        await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
        await using var db = new FlightsDbContext(_fixture.Options);
        var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        row.BookedAt.ShouldBe(at.AddTicks(-7));
        (await reconciler.ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
    }

    private ServiceProvider Maintenance(
        bool exclusive = true,
        SaveChangesInterceptor? interceptor = null
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<global::Marten.IDocumentStore>(_fixture.Store);
        using var db = new FlightsDbContext(_fixture.Options);
        services.AddBookingReadModelMaintenance(db.Database.GetConnectionString()!, exclusive);
        if (interceptor is not null)
            services.ConfigureDbContext<FlightsDbContext>(options =>
                options.AddInterceptors(interceptor)
            );
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("owner")]
    [InlineData("ahead")]
    [InlineData("ownerless")]
    [InlineData("quote")]
    [InlineData("unsupported")]
    public async Task Production_maintenance_registration_validates_and_repairs_only_derived_data(
        string scenario
    )
    {
        var owner = Guid.NewGuid();
        var id = await _fixture.SeedAsync(scenario is "ownerless" or "quote" ? null : owner);
        if (scenario == "ownerless")
            await _fixture.AppendAsync(id, BookingReconcilerFixture.Held(null));
        using var provider = Maintenance();
        using var scope = provider.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOrderReadModelReconciler>();
        Guid? rowId = null;
        if (scenario is "corrupt" or "owner" or "ahead" or "unsupported")
        {
            await reconciler.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
            await using var db = new FlightsDbContext(_fixture.Options);
            var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            rowId = row.Id;
            if (scenario == "owner")
                row.UserId = Guid.NewGuid();
            else if (scenario == "ahead")
                row.ProjectedStreamVersion = 99;
            else
                row.TotalAmount = 777;
            await db.SaveChangesAsync(Ct);
        }
        if (scenario == "unsupported")
            await _fixture.AppendAsync(id, new UnsupportedBookingEvent("unknown"));
        var before = await reconciler.ValidateAsync(id, Ct);
        if (scenario == "quote")
            before.Issues.ShouldBeEmpty();
        else
            before.Issues.ShouldNotBeEmpty();
        var report = await scope
            .ServiceProvider.GetRequiredService<IOrderReadModelRebuildRunner>()
            .RunAsync(true, true, Ct);
        var result = report.Streams.Single(x => x.AggregateId == id);
        if (scenario is "ownerless" or "unsupported")
        {
            result.Issues.ShouldContain(x => !x.RepairableByReset);
            report.Failed.ShouldBeGreaterThan(0);
        }
        else
            result.Issues.ShouldBeEmpty();
        await using var verify = new FlightsDbContext(_fixture.Options);
        var actual = await verify
            .Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AggregateId == id, Ct);
        if (scenario is "ownerless" or "quote")
            actual.ShouldBeNull();
        else
        {
            actual.ShouldNotBeNull();
            if (rowId is not null)
                actual.Id.ShouldBe(rowId.Value);
            actual.TotalAmount.ShouldBe(
                scenario == "unsupported" ? 777 : BookingReconcilerFixture.Amount.Amount
            );
            actual.ProjectedStreamVersion.ShouldBe(2);
            actual.UserId.ShouldBe(owner);
        }
        await using var source = _fixture.Store.QuerySession();
        (await source.Events.FetchStreamAsync(id, token: Ct)).Count.ShouldBe(
            scenario == "quote" ? 1
            : scenario == "unsupported" ? 3
            : 2
        );
    }

    [Fact]
    public async Task Failed_reset_retains_original_row_id_fields_and_checkpoint()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        using (var provider = Maintenance())
        using (var scope = provider.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
                .ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        Guid rowId;
        await using (var db = new FlightsDbContext(_fixture.Options))
        {
            var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            rowId = row.Id;
            row.TotalAmount = 777;
            await db.SaveChangesAsync(Ct);
        }
        using var failing = Maintenance(interceptor: new FailedSave());
        using var scope2 = failing.CreateScope();
        var report = await scope2
            .ServiceProvider.GetRequiredService<IOrderReadModelRebuildRunner>()
            .RunAsync(true, true, Ct);
        report.Streams.Single(x => x.AggregateId == id).Issues.ShouldNotBeEmpty();
        await using var verify = new FlightsDbContext(_fixture.Options);
        var unchanged = await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct);
        unchanged.Id.ShouldBe(rowId);
        unchanged.TotalAmount.ShouldBe(777);
        unchanged.ProjectedStreamVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Drain_incremental_then_reset_same_version_corruption_then_resume_next_event()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        var gate = new DrainGate();
        using var normal = Maintenance(false, gate);
        using var normalScope = normal.CreateScope();
        var incremental =
            normalScope.ServiceProvider.GetRequiredService<IOrderReadModelReconciler>();
        var inFlight = incremental.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
            inFlight.IsCompleted.ShouldBeFalse();
            gate.Release.TrySetResult();
            await inFlight; // drain; no normal writer remains when Reset begins
            await using (var db = new FlightsDbContext(_fixture.Options))
            {
                var row = await db.Orders.SingleAsync(x => x.AggregateId == id, Ct);
                row.TotalAmount = 999;
                await db.SaveChangesAsync(Ct);
            }
            using (var maintenance = Maintenance())
            using (var scope = maintenance.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<IOrderReadModelReconciler>()
                    .ReconcileAsync(id, OrderReadModelReconcileMode.Reset, Ct);
            await _fixture.AppendAsync(
                id,
                new PaymentAuthorized(
                    PaymentRef.New(),
                    BookingReconcilerFixture.Amount,
                    BookingReconcilerFixture.Now
                )
            );
            await incremental.ReconcileAsync(id, OrderReadModelReconcileMode.Incremental, Ct);
            (await incremental.ValidateAsync(id, Ct)).Issues.ShouldBeEmpty();
            await using var verify = new FlightsDbContext(_fixture.Options);
            var row2 = await verify.Orders.SingleAsync(x => x.AggregateId == id, Ct);
            row2.TotalAmount.ShouldBe(100);
            row2.ProjectedStreamVersion.ShouldBe(3);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
    }

    private sealed class FailedSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("test-only failure");
    }

    private sealed class DrainGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    [Fact]
    public async Task Dry_run_reports_missing_rows_without_mutation_and_execute_requires_exclusion()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        var runner = new OrderReadModelRebuildRunner(
            new BookingStreamCatalog(_fixture.Store),
            new OrderReadModelReconciler(
                _fixture.Store,
                _fixture.Options,
                new BookingProjectionMaintenanceContext()
            )
        );
        var report = await runner.RunAsync(false, false, Ct);
        report.Failed.ShouldBeGreaterThan(0);
        report.Streams.ShouldContain(x =>
            x.AggregateId == id && x.Issues.Any(i => i.Code == "ProjectionMissing")
        );
        await Should.ThrowAsync<BookingProjectionTerminalException>(() =>
            runner.RunAsync(true, false, Ct)
        );
        await using var db = new FlightsDbContext(_fixture.Options);
        (await db.Orders.AnyAsync(x => x.AggregateId == id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Catalog_yields_each_booking_once_and_excludes_other_stream_types()
    {
        var id = await _fixture.SeedAsync(Guid.NewGuid());
        var foreign = Guid.NewGuid();
        await using (var session = _fixture.Store.LightweightSession())
        {
            session.Events.StartStream<UnsupportedBookingEvent>(
                foreign,
                new UnsupportedBookingEvent("foreign")
            );
            await session.SaveChangesAsync(Ct);
        }
        var ids = new List<Guid>();
        await foreach (var stream in new BookingStreamCatalog(_fixture.Store).ReadIdsAsync(Ct))
            ids.Add(stream);
        ids.Count(x => x == id).ShouldBe(1);
        ids.ShouldNotContain(foreign);
        ids.Distinct().Count().ShouldBe(ids.Count);
    }
}
