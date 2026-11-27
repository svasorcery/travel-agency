using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class SavedOptionPostgres : IntegrationTestBase
{
    private readonly TripsTestRing ring = new();
    private TripsHttpFixture? http;
    private IInterceptor activeInterceptor = null!;

    internal async Task<TripsHttpFixture> Http(IInterceptor interceptor)
    {
        activeInterceptor = interceptor;
        return http ??= await TripsHttpFixture.Start(
            environment: "Testing",
            itinerary: true,
            optionPreview: true,
            optionProfile: "spb-option",
            optionCatalogue: "B",
            saveOptions: true,
            configure: services =>
            {
                services.AddScoped(_ => Db(activeInterceptor));
                services.AddScoped<ITripPlanStore, TripPlanStore>();
                services.AddScoped<ISavedOptionStore, TripPlanStore>();
                services.AddSingleton<ITripPlanProtector>(ring.Protector);
                services.AddSingleton<ISavedOptionProtection>(
                    new SavedOptionProtection(ring.Provider)
                );
                services.AddSingleton(TimeProvider.System);
                services.AddScoped<SavedOptionService>();
            }
        );
    }

    protected override async ValueTask OnDisposingAsync()
    {
        try
        {
            if (http is not null)
                await http.DisposeAsync();
        }
        finally
        {
            ring.Dispose();
        }
    }

    internal TripsDbContext Db(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(ConnectionString);
        TripsDbContextConfiguration.Configure(options);
        options.AddInterceptors(interceptors);
        return new(options.Options);
    }

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }
}

[Collection("TripsPostgreSql")]
[Trait("Category", "Integration")]
public sealed class SavedOptionPersistenceTests(SavedOptionPostgres postgres)
    : IClassFixture<SavedOptionPostgres>,
        IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static OwnerIdentity Owner =>
        OwnerIdentity.Create(TripsHttpFixture.Issuer, TripsHttpFixture.Owner).Value;
    private static readonly OwnerIdentity Other = OwnerIdentity
        .Create(Owner.Issuer, Guid.NewGuid())
        .Value;
    private readonly Guid selected = Guid.NewGuid();
    private readonly Guid ordinary = Guid.NewGuid();
    private readonly HashSet<Guid> seeded = [];

    public async ValueTask InitializeAsync()
    {
        await using var db = Db();
        // This serial collection owns the entire disposable database for this class.
        await db.SavedOptionReceipts.ExecuteDeleteAsync(Ct);
        await db.Plans.ExecuteDeleteAsync(Ct);
        for (int i = 0; i < 99; i++)
        {
            var id = Guid.NewGuid();
            seeded.Add(id);
            db.Plans.Add(
                new TripPlanEntity
                {
                    TripId = id,
                    OwnerIssuer = Owner.Issuer,
                    OwnerSubject = Owner.Subject,
                    Revision = 1,
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch,
                    ProtectedPayload = "fictional-plan",
                }
            );
        }
        await db.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private TripsDbContext Db(params IInterceptor[] interceptors) => postgres.Db(interceptors);

    private static TripPlanRecord Plan(Guid id, OwnerIdentity owner) =>
        new(id, owner, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "fictional-plan");

    private Task<ErrorOr<SavedOptionWrite>> Save(
        TripPlanStore store,
        DateTimeOffset expiresAt,
        bool allowFirst = true
    ) =>
        store.TrySaveAsync(
            Owner,
            selected,
            expiresAt,
            _ =>
                allowFirst
                    ? Task.FromResult<ErrorOr<NewSavedOption>>(
                        new NewSavedOption(Plan(selected, Owner), "fictional-option")
                    )
                    : throw new Xunit.Sdk.XunitException("Replay reached first-write preparation."),
            Ct
        );

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Last_slot_is_atomic_with_a_blocked_waiter(
        bool firstSelection,
        bool waitingSelection,
        bool rollback
    )
    {
        var gate = new CommitGate(rollback);
        var fixture = await postgres.Http(gate);
        var brief = new
        {
            destinationId = "saint-petersburg",
            originId = "moscow",
            startDate = "2026-10-28",
            endDate = "2026-10-30",
            adults = 2,
            currency = "RUB",
            totalBudget = (string?)null,
            interests = new[] { "architecture" },
            pace = "relaxed",
        };
        using var previewRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/trips/option-preview"
        )
        {
            Content = JsonContent.Create(new { brief }),
        };
        previewRequest.Headers.Add("X-Trips-Option-Save", "1");
        using var preview = await fixture.Client.SendAsync(previewRequest, Ct);
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var offered = JsonDocument.Parse(await preview.Content.ReadAsByteArrayAsync(Ct));
        var selection = offered.RootElement.GetProperty("selection");
        var selectionId = selection.GetProperty("tripId").GetGuid();
        var token = selection.GetProperty("selectionToken").GetString();
        Task<HttpResponseMessage> Send(bool option) =>
            option
                ? fixture.Client.PostAsJsonAsync(
                    "/api/trips/saved-options",
                    new { selectionToken = token },
                    Ct
                )
                : fixture.Client.PostAsJsonAsync(
                    "/api/trips",
                    new { title = "Fictional manual plan", brief },
                    Ct
                );
        await using var observer = Db();
        var first = Send(firstSelection);
        if (
            await Task.WhenAny(first, gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct))
            == first
        )
            throw new Xunit.Sdk.XunitException(
                $"Holder returned HTTP {(int)(await first).StatusCode} before commit."
            );
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(1), Ct);
        var second = Send(waitingSelection);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (
                !await observer
                    .Database.SqlQuery<bool>(
                        $"SELECT EXISTS (SELECT FROM pg_locks WHERE database=(SELECT oid FROM pg_database WHERE datname=current_database()) AND locktype='advisory' AND NOT granted AND cardinality(pg_blocking_pids(pid))>0) AS \"Value\""
                    )
                    .SingleAsync(timeout.Token)
            )
                await Task.Delay(20, timeout.Token);
            (await observer.Plans.CountAsync(Ct)).ShouldBe(99);
            (await observer.SavedOptionReceipts.CountAsync(Ct)).ShouldBe(0);
            using var otherRequest = new HttpRequestMessage(HttpMethod.Post, "/api/trips")
            {
                Content = JsonContent.Create(new { title = "Fictional independent plan", brief }),
            };
            otherRequest.Headers.Add("X-Trips-Owner", Other.Subject.ToString());
            using var other = await fixture.Client.SendAsync(otherRequest, Ct);
            other.StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var firstResult = await first;
        using var waitingResult = await second;
        firstResult.StatusCode.ShouldBe(
            rollback ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created
        );
        var same = firstSelection && waitingSelection;
        waitingResult.StatusCode.ShouldBe(
            rollback ? HttpStatusCode.Created
            : same ? HttpStatusCode.OK
            : HttpStatusCode.Conflict
        );
        using var winning = JsonDocument.Parse(
            await (rollback ? waitingResult : firstResult).Content.ReadAsByteArrayAsync(Ct)
        );
        var winnerId = winning.RootElement.GetProperty("tripId").GetGuid();
        var winnerIsSelection = rollback ? waitingSelection : firstSelection;
        if (winnerIsSelection)
            winnerId.ShouldBe(selectionId);
        else
            winnerId.ShouldNotBe(selectionId);
        using var competing = JsonDocument.Parse(
            await waitingResult.Content.ReadAsByteArrayAsync(Ct)
        );
        if (!rollback && same)
            competing.RootElement.GetProperty("tripId").GetGuid().ShouldBe(selectionId);
        if (!rollback && !same)
            competing
                .RootElement.GetProperty("code")
                .GetString()
                .ShouldBe("Trips.CapacityExceeded");
        using var retry = await Send(true);
        retry.StatusCode.ShouldBe(winnerIsSelection ? HttpStatusCode.OK : HttpStatusCode.Conflict);
        using var retried = JsonDocument.Parse(await retry.Content.ReadAsByteArrayAsync(Ct));
        if (winnerIsSelection)
            retried.RootElement.GetProperty("tripId").GetGuid().ShouldBe(selectionId);
        else
            retried.RootElement.GetProperty("code").GetString().ShouldBe("Trips.CapacityExceeded");
        var rows = await observer
            .Plans.AsNoTracking()
            .Where(x => x.OwnerSubject == Owner.Subject)
            .ToArrayAsync(Ct);
        rows.Length.ShouldBe(100);
        rows.Select(x => x.TripId).ToHashSet().SetEquals(seeded.Append(winnerId)).ShouldBeTrue();
        rows.All(x => x.Revision == 1).ShouldBeTrue();
        rows.Where(x => x.ProtectedOption != null)
            .Select(x => x.TripId)
            .ShouldBe(winnerIsSelection ? [selectionId] : Array.Empty<Guid>());
        (await observer.SavedOptionReceipts.Select(x => x.TripId).ToArrayAsync(Ct)).ShouldBe(
            winnerIsSelection ? [selectionId] : Array.Empty<Guid>()
        );
        (await observer.Plans.CountAsync(x => x.OwnerSubject == Other.Subject, Ct)).ShouldBe(1);
        if (same && !rollback)
            await EditAndReplay(fixture.Client, selectionId, token!, brief);
    }

    private static async Task EditAndReplay(HttpClient client, Guid id, string token, object brief)
    {
        var path = $"/api/trips/{id}";
        var history = await client.GetByteArrayAsync(path + "/saved-option", Ct);
        var days = new[]
        {
            new
            {
                date = "2026-10-28",
                blocks = new object[]
                {
                    new
                    {
                        blockId = Guid.NewGuid(),
                        title = "Fictional evening",
                        note = "Manual note",
                        startTime = "21:30",
                        endTime = "22:00",
                        estimate = new { amount = "450.00", currency = "RUB" },
                    },
                },
            },
            new { date = "2026-10-29", blocks = Array.Empty<object>() },
            new { date = "2026-10-30", blocks = Array.Empty<object>() },
        };
        using var update = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(
                new
                {
                    title = "Fictional edited trip",
                    brief,
                    days,
                }
            ),
        };
        update.Headers.Add("If-Match", "\"1\"");
        using var edited = await client.SendAsync(update, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        edited.Headers.ETag!.Tag.ShouldBe("\"2\"");
        using var detail = JsonDocument.Parse(await edited.Content.ReadAsByteArrayAsync(Ct));
        detail.RootElement.GetProperty("knownUserEstimateSubtotal").GetString().ShouldBe("450.00");
        using var replay = await client.PostAsJsonAsync(
            "/api/trips/saved-options",
            new { selectionToken = token, title = "Fictional retry" },
            Ct
        );
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var location = JsonDocument.Parse(await replay.Content.ReadAsByteArrayAsync(Ct));
        location.RootElement.GetProperty("tripId").GetGuid().ShouldBe(id);
        using var reopened = await client.GetAsync(path, Ct);
        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);
        reopened.Headers.ETag!.Tag.ShouldBe("\"2\"");
        using var actual = JsonDocument.Parse(await reopened.Content.ReadAsByteArrayAsync(Ct));
        JsonElement.DeepEquals(actual.RootElement, detail.RootElement).ShouldBeTrue();
        (await client.GetByteArrayAsync(path + "/saved-option", Ct))
            .AsSpan()
            .SequenceEqual(history)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Replay_of_an_edited_plan_survives_expiry_and_cleanup_but_never_resurrects_a_delete()
    {
        await using var db = Db();
        var store = new TripPlanStore(db);
        var expires = await WallTime(db) + TimeSpan.FromSeconds(2);
        (await Save(store, expires)).Value.Created.ShouldBeTrue();
        var changed = Plan(selected, Owner) with
        {
            Revision = 2,
            ProtectedPayload = "fictional-edited",
        };
        (await store.TryReplaceAsync(1, changed, Ct)).IsError.ShouldBeFalse();
        await WaitUntil(db, expires);
        (await Save(store, expires, allowFirst: false)).Value.ShouldBe(
            new SavedOptionWrite(selected, false)
        );
        (await store.PruneExpiredReceiptsAsync(Ct)).ShouldBe(1);
        (await Save(store, expires, allowFirst: false)).Value.ShouldBe(
            new SavedOptionWrite(selected, false)
        );
        (await store.LoadAsync(Owner, selected, Ct)).Value.ShouldBe(changed);
        (
            await ((ISavedOptionStore)store).LoadAsync(Owner, selected, Ct)
        ).Value.ProtectedOption.ShouldBe("fictional-option");
        (await store.TryDeleteAsync(Owner, selected, 2, Ct)).IsError.ShouldBeFalse();
        (await Save(store, expires, allowFirst: false)).FirstError.ShouldBe(
            TripErrors.SelectionUnavailable
        );
    }

    [Fact]
    public async Task A_receipt_blocks_recreation_before_pruning_and_postgres_wall_time_blocks_it_afterwards()
    {
        await using var db = Db();
        var store = new TripPlanStore(db);
        var expires = await WallTime(db) + TimeSpan.FromSeconds(2);
        (await Save(store, expires)).Value.Created.ShouldBeTrue();
        (await store.TryDeleteAsync(Owner, selected, 1, Ct)).IsError.ShouldBeFalse();
        (await Save(store, expires, allowFirst: false)).FirstError.ShouldBe(
            TripErrors.SelectionUnavailable
        );
        (await store.PruneExpiredReceiptsAsync(Ct)).ShouldBe(0);
        await WaitUntil(db, expires);
        (await store.PruneExpiredReceiptsAsync(Ct)).ShouldBe(1);
        (await Save(store, expires, allowFirst: false)).FirstError.ShouldBe(
            TripErrors.SelectionUnavailable
        );
        (await db.Plans.CountAsync(Ct)).ShouldBe(99);
        (await db.SavedOptionReceipts.CountAsync(Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Admission_after_wait_uses_database_wall_time_and_resolves_committed_replay_first(
        bool previouslyCommitted
    )
    {
        await using var observer = Db();
        await using var waiter = Db();
        var pid = await Pid(waiter);
        var expires = await WallTime(observer) + TimeSpan.FromSeconds(3);
        if (previouslyCommitted)
            (await Save(new TripPlanStore(observer), expires)).Value.Created.ShouldBeTrue();
        var gate = new CommitGate(false);
        await using var holder = Db(gate);
        // At full capacity the ordinary writer cannot reach COMMIT, so free one
        // ordinary slot while retaining the saved plan for the replay variant.
        if (previouslyCommitted)
            (
                await new TripPlanStore(observer).TryDeleteAsync(Owner, seeded.First(), 1, Ct)
            ).IsError.ShouldBeFalse();
        var first = new TripPlanStore(holder).TryCreateAsync(Plan(ordinary, Owner), 100, Ct);
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        var waiting = Save(new TripPlanStore(waiter), expires, allowFirst: false);
        try
        {
            await ObserveBlocked(observer, pid);
            (await WallTime(observer) < expires).ShouldBeTrue();
            await WaitUntil(observer, expires);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        (await first).IsError.ShouldBeFalse();
        var outcome = await waiting;
        if (previouslyCommitted)
            outcome.Value.ShouldBe(new SavedOptionWrite(selected, false));
        else
            outcome.FirstError.ShouldBe(TripErrors.SelectionUnavailable);
        (await observer.Plans.CountAsync(Ct)).ShouldBe(100);
        (await observer.SavedOptionReceipts.CountAsync(Ct)).ShouldBe(previouslyCommitted ? 1 : 0);
    }

    [Fact]
    public async Task Null_absence_and_foreign_rows_are_distinct_from_unreadable_envelopes()
    {
        await using var db = Db();
        ISavedOptionStore store = new TripPlanStore(db);
        var id = seeded.First();
        (await store.LoadAsync(Owner, id, Ct)).Value.ProtectedOption.ShouldBeNull();
        (await store.LoadAsync(Other, id, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        (await store.LoadAsync(Owner, Guid.NewGuid(), Ct)).FirstError.ShouldBe(TripErrors.NotFound);
        var exact = new string('я', NewSavedOption.MaximumEnvelopeBytes / 2);
        await db
            .Plans.Where(x => x.TripId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProtectedOption, exact), Ct);
        var atLimit = await store.LoadAsync(Owner, id, Ct);
        atLimit.IsError.ShouldBeFalse();
        (atLimit.Value.ProtectedOption == exact).ShouldBeTrue();
        foreach (
            var corrupt in new[]
            {
                "",
                new string('я', NewSavedOption.MaximumEnvelopeBytes / 2 + 1),
            }
        )
        {
            await db
                .Plans.Where(x => x.TripId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProtectedOption, corrupt), Ct);
            (await store.LoadAsync(Owner, id, Ct)).FirstError.ShouldBe(TripErrors.Unavailable);
            (await store.LoadAsync(Other, id, Ct)).FirstError.ShouldBe(TripErrors.NotFound);
            (await new TripPlanStore(db).LoadAsync(Owner, id, Ct)).Value!.ProtectedPayload.ShouldBe(
                "fictional-plan"
            );
        }
    }

    [Fact]
    public async Task Readiness_requires_the_actual_nullable_column_receipt_key_and_expiry_index()
    {
        using var ring = new TripsTestRing();
        using var container = new ServiceCollection().AddScoped(_ => Db()).BuildServiceProvider();
        var readiness = new TripsReadiness(
            container.GetRequiredService<IServiceScopeFactory>(),
            ring.Provider
        );
        await using var db = Db();
        (await readiness.IsReadyAsync(Ct)).ShouldBeTrue();
        foreach (
            var (damage, repair) in new[]
            {
                (
                    "ALTER TABLE trips.trip_plans RENAME COLUMN protected_option TO hidden_option",
                    "ALTER TABLE trips.trip_plans RENAME COLUMN hidden_option TO protected_option"
                ),
                (
                    "ALTER TABLE trips.saved_option_receipts DROP CONSTRAINT pk_saved_option_receipts",
                    "ALTER TABLE trips.saved_option_receipts ADD CONSTRAINT pk_saved_option_receipts PRIMARY KEY (trip_id)"
                ),
                (
                    "DROP INDEX trips.ix_saved_option_receipts_expires_at",
                    "CREATE INDEX ix_saved_option_receipts_expires_at ON trips.saved_option_receipts(expires_at)"
                ),
            }
        )
        {
            await db.Database.ExecuteSqlRawAsync(damage, Ct);
            try
            {
                (await readiness.IsReadyAsync(Ct)).ShouldBeFalse();
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(repair, Ct);
            }
            (await readiness.IsReadyAsync(Ct)).ShouldBeTrue();
        }
    }

    private static async Task<int> Pid(TripsDbContext db)
    {
        await db.Database.OpenConnectionAsync(Ct);
        return await db
            .Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(Ct);
    }

    private static Task<DateTimeOffset> WallTime(TripsDbContext db) =>
        db
            .Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(Ct);

    private static async Task WaitUntil(TripsDbContext observer, DateTimeOffset expires)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (await WallTime(observer) < expires)
            await Task.Delay(20, deadline.Token);
    }

    private static async Task ObserveBlocked(TripsDbContext observer, int pid)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (
            !await observer
                .Database.SqlQuery<bool>(
                    $"SELECT cardinality(pg_blocking_pids({pid})) > 0 AS \"Value\""
                )
                .SingleAsync(deadline.Token)
        )
            await Task.Delay(20, deadline.Token);
    }

    private sealed class CommitGate(bool rollback) : DbTransactionInterceptor
    {
        public TaskCompletionSource Arrived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Arrived.TrySetResult())
                return result;
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            if (rollback)
                throw new InvalidOperationException("Fixture rollback before commit.");
            return result;
        }
    }
}
