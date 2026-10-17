using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace Travel.Modules.Hotels.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class HotelDurablePurchaseTests(HotelStoreFixture fixture)
    : IClassFixture<HotelStoreFixture>
{
    [Fact]
    public async Task Later_drafts_cannot_hide_an_unresolved_or_completed_purchase_from_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var now = DateTimeOffset.UtcNow;
        var pending = Seed(now.AddDays(-10), ReservationOutcome.Pending);
        var confirmed = Seed(now.AddDays(-9), ReservationOutcome.Confirmed);
        db.Reservations.AddRange(
            new[] { pending, confirmed }.Concat(
                Enumerable
                    .Range(0, 60)
                    .Select(i => Seed(now.AddMinutes(i), ReservationOutcome.Quoted))
            )
        );
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var listed = await scope
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .ListAsync(owner, ct);
        listed.IsError.ShouldBeFalse();
        listed.Value.Length.ShouldBe(50);
        listed.Value[0].Id.ShouldBe(pending.Id);
        listed.Value[1].Id.ShouldBe(confirmed.Id);

        HotelReservationEntity Seed(DateTimeOffset created, ReservationOutcome outcome) =>
            new()
            {
                Id = Guid.NewGuid(),
                OwnerId = owner,
                Version = 1,
                QuoteRevision = 1,
                Provider = "fictional",
                Environment = "Test",
                CreatedAt = created,
                UpdatedAt = created,
                ValidUntil = now.AddMinutes(5),
                StateJson = JsonSerializer.Serialize(new ReservationState { Outcome = outcome }),
                AttemptId = outcome == ReservationOutcome.Quoted ? null : Guid.NewGuid(),
                ActionId = outcome == ReservationOutcome.Quoted ? null : Guid.NewGuid(),
                AdmittedAt = outcome == ReservationOutcome.Quoted ? null : created,
                PrivatePurgeAt = outcome == ReservationOutcome.Pending ? null : now.AddDays(90),
            };
    }

    [Fact]
    public async Task Cleanup_with_stale_selection_cannot_erase_an_intervening_admission()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await service.QuoteAsync(
                owner,
                id,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                ct
            )
        ).IsError.ShouldBeFalse();
        var otherId = Guid.NewGuid();
        (
            await service.QuoteAsync(
                Guid.NewGuid(),
                otherId,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                ct
            )
        ).IsError.ShouldBeFalse();
        var sharedDb = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        await sharedDb
            .Reservations.Where(row => row.Id == otherId)
            .ExecuteUpdateAsync(
                update =>
                    update
                        .SetProperty(row => row.PrivatePurgeAt, DateTimeOffset.UtcNow.AddHours(12))
                        .SetProperty(row => row.Version, row => row.Version + 1),
                ct
            );
        var pause = new PauseCleanupUpdate(id);
        await using var cleanupDb = new HotelsDbContext(
            new DbContextOptionsBuilder<HotelsDbContext>()
                .UseNpgsql(fixture.Connection)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(pause)
                .Options
        );
        var cleanup = new HotelReservationStore(
            cleanupDb,
            scope.ServiceProvider.GetRequiredService<IDbContextOutbox<HotelsDbContext>>()
        );
        // Other shared-fixture rows may be due. Pause this target's stale UPDATE only.
        var purge = cleanup.PurgePrivateContentAsync(DateTimeOffset.UtcNow.AddDays(2), ct);
        await Task.WhenAny(pause.Selected.Task, purge).WaitAsync(TimeSpan.FromSeconds(10), ct);
        if (purge.IsCompleted)
            await purge; // Surface a fixture SQL error before waiting for its barrier.
        pause.Selected.Task.IsCompleted.ShouldBeTrue();
        try
        {
            (
                await service.PurchaseAsync(
                    owner,
                    id,
                    new(
                        Guid.NewGuid(),
                        1,
                        true,
                        new(
                            [new("Fictional", "Traveller")],
                            "fictional@example.invalid",
                            "+44000000000",
                            true,
                            true
                        )
                    ),
                    ct
                )
            ).IsError.ShouldBeFalse();
        }
        finally
        {
            pause.Resume.TrySetResult();
        }
        var removed = await purge;
        var retained = await sharedDb
            .Reservations.AsNoTracking()
            .Where(row => row.Id == id || row.Id == otherId)
            .Select(row => new
            {
                row.Id,
                row.Version,
                row.AttemptId,
                termsPresent = row.TermsJson != null,
                contextPresent = row.ProtectedContext != null,
                partyPresent = row.ProtectedParty != null,
            })
            .ToArrayAsync(ct);
        TestContext.Current.TestOutputHelper?.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    removed,
                    target = id,
                    other = otherId,
                    retained,
                    pause.TargetUpdateCalls,
                    pause.TargetRowsAffected,
                }
            )
        );
        removed.ShouldBeGreaterThanOrEqualTo(1); // Other due drafts are legitimately purged.
        pause.TargetUpdateCalls.ShouldBe(1);
        pause.TargetRowsAffected.ShouldBe(0);
        var other = retained.Single(row => row.Id == otherId);
        other.AttemptId.ShouldBeNull();
        other.termsPresent.ShouldBeFalse();
        other.contextPresent.ShouldBeFalse();
        other.partyPresent.ShouldBeFalse();
        var row = (
            await scope
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .LoadAsync(id, owner, ct)
        ).Value!;
        row.AttemptId.ShouldNotBeNull();
        row.Terms.ShouldNotBeNull();
        row.ProtectedParty.ShouldNotBeNull();
        row.ProtectedContext.ShouldNotBeNull();
        row.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
    }

    private sealed class PauseCleanupUpdate(Guid targetId) : DbCommandInterceptor
    {
        public int TargetUpdateCalls { get; private set; }
        public int? TargetRowsAffected { get; private set; }
        public TaskCompletionSource Selected { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (IsTarget(command))
            {
                TargetUpdateCalls++;
                Selected.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (IsTarget(command))
                TargetRowsAffected = result;
            return ValueTask.FromResult(result);
        }

        private bool IsTarget(DbCommand command) =>
            command.CommandText.StartsWith("UPDATE hotels.reservations", StringComparison.Ordinal)
            && command
                .Parameters.Cast<DbParameter>()
                .Any(parameter => parameter.Value is Guid value && value == targetId);
    }

    [Fact]
    public async Task Quote_replacement_and_purchase_race_keeps_one_revision_and_never_overwrites_an_admitted_purchase()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var terms = DuffelStaysProviderTests.Terms();
        var selection = new SelectedStay(
            DuffelStaysProviderTests.Request(),
            new(fixture.Provider.Key, "Test", "rate_fictional", terms, null, true, null)
        );
        using (var scope = fixture.Host.Services.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<HotelReservationService>()
                    .QuoteAsync(owner, id, null, selection, ct)
            ).IsError.ShouldBeFalse();
        using var buying = fixture.Host.Services.CreateScope();
        using var replacing = fixture.Host.Services.CreateScope();
        var before = fixture.Provider.Creates;
        var purchase = buying
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .PurchaseAsync(
                owner,
                id,
                new(
                    Guid.NewGuid(),
                    1,
                    true,
                    new(
                        [new("Fictional", "Traveller")],
                        "fictional@example.invalid",
                        "+44000000000",
                        true,
                        true
                    )
                ),
                ct
            );
        var quote = replacing
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .QuoteAsync(
                owner,
                id,
                1,
                selection with
                {
                    Offer = selection.Offer with
                    {
                        Terms = terms with { Room = "Changed at the same price" },
                    },
                },
                ct
            );
        await Task.WhenAll(purchase, quote);
        var purchaseResult = await purchase;
        var quoteResult = await quote;
        (purchaseResult.IsError ^ quoteResult.IsError).ShouldBeTrue();
        using var check = fixture.Host.Services.CreateScope();
        var row = (
            await check
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .LoadAsync(id, owner, ct)
        ).Value!;
        if (row.AttemptId is not null)
        {
            row.QuoteRevision.ShouldBe(1);
            row.Terms!.Room.ShouldBe(terms.Room);
            (fixture.Provider.Creates - before).ShouldBe(1);
        }
        else
        {
            row.QuoteRevision.ShouldBe(2);
            row.Terms!.Room.ShouldBe("Changed at the same price");
            fixture.Provider.Creates.ShouldBe(before);
        }
    }

    [Fact]
    public async Task Crash_after_atomic_claim_does_not_recreate_on_replay_new_key_or_private_retention()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var action = Guid.NewGuid();
        var before = fixture.Provider.Creates;
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
            (
                await service.QuoteAsync(
                    owner,
                    id,
                    null,
                    new(
                        DuffelStaysProviderTests.Request(),
                        new(
                            fixture.Provider.Key,
                            "Test",
                            "rate_fictional",
                            DuffelStaysProviderTests.Terms(),
                            null,
                            true,
                            null
                        )
                    ),
                    ct
                )
            ).IsError.ShouldBeFalse();
            var store = scope.ServiceProvider.GetRequiredService<IHotelReservationStore>();
            var row = (await store.LoadAsync(id, owner, ct)).Value!;
            row.AttemptId = Guid.NewGuid();
            row.ActionId = action;
            row.AdmittedAt = DateTimeOffset.UtcNow;
            row.PrivatePurgeAt = null;
            row.State.Outcome = ReservationOutcome.Submitted;
            row.State.EscalateAt = DateTimeOffset.UtcNow.AddMinutes(10);
            row.ProtectedParty = scope
                .ServiceProvider.GetRequiredService<IHotelPrivateProtector>()
                .Protect(
                    JsonSerializer.Serialize(party),
                    HotelReservationService.Binding(row, "party")
                )
                .Value;
            (
                await store.SaveAsync(
                    row,
                    row.Version,
                    [new(id, true, row.State.EscalateAt.Value)],
                    ct
                )
            ).Value.ShouldBeTrue(); // Process dies before any create invocation.
        }
        using var replacement = fixture.Host.Services.CreateScope();
        var resumed = replacement.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await resumed.PurchaseAsync(owner, id, new(action, 1, true, party), ct)
        ).Value.AttemptId.ShouldNotBeNull();
        (
            await resumed.PurchaseAsync(owner, id, new(Guid.NewGuid(), 1, true, party), ct)
        ).FirstError.ShouldBe(HotelErrors.Conflict);
        await replacement
            .ServiceProvider.GetRequiredService<HotelReservationStore>()
            .PurgePrivateContentAsync(DateTimeOffset.UtcNow.AddYears(10), ct);
        (await resumed.DetailAsync(owner, id, ct)).Value.Accepted.ShouldNotBeNull();
        fixture.Provider.Creates.ShouldBe(before);
    }

    [Fact]
    public async Task Failed_atomic_admission_commits_neither_claim_nor_outbox_and_releases_permit()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await service.QuoteAsync(
                owner,
                id,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                ct
            )
        ).IsError.ShouldBeFalse();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        // Test-owned fault is in the same disposable database, at the actual transaction boundary.
        var function = "reject_hotels_" + id.ToString("N");
        var createFault =
            $"CREATE FUNCTION hotels.{function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.id = '{id}'::uuid AND NEW.attempt_id IS NOT NULL THEN RAISE EXCEPTION 'fictional admission rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER {function} BEFORE UPDATE ON hotels.reservations FOR EACH ROW EXECUTE FUNCTION hotels.{function}();";
        await db.Database.ExecuteSqlRawAsync(createFault, ct);
        var before = await WorkCount();
        var creates = fixture.Provider.Creates;
        try
        {
            var result = await service.PurchaseAsync(
                owner,
                id,
                new(
                    Guid.NewGuid(),
                    1,
                    true,
                    new(
                        [new("Fictional", "Traveller")],
                        "fictional@example.invalid",
                        "+44000000000",
                        true,
                        true
                    )
                ),
                ct
            );
            result.IsError.ShouldBeTrue();
            fixture.Capacity.Active.ShouldBe(0);
            fixture.Provider.Creates.ShouldBe(creates);
            (await WorkCount()).ShouldBe(before);
            (
                await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id, ct)
            ).AttemptId.ShouldBeNull();
        }
        finally
        {
            var removeFault =
                $"DROP TRIGGER {function} ON hotels.reservations; DROP FUNCTION hotels.{function}();";
            await db.Database.ExecuteSqlRawAsync(removeFault, ct);
        }
    }

    [Fact]
    public async Task Fifty_valid_contenders_with_capacity_for_everyone_create_exactly_once()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var terms = DuffelStaysProviderTests.Terms();
        using (var scope = fixture.Host.Services.CreateScope())
        {
            var quote = await scope
                .ServiceProvider.GetRequiredService<HotelReservationService>()
                .QuoteAsync(
                    owner,
                    id,
                    null,
                    new(
                        DuffelStaysProviderTests.Request(),
                        new(fixture.Provider.Key, "Test", "rate_fictional", terms, null, true, null)
                    ),
                    TestContext.Current.CancellationToken
                );
            quote.IsError.ShouldBeFalse();
        }
        var purchase = new HotelPurchase(
            Guid.NewGuid(),
            1,
            true,
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            )
        );
        var before = fixture.Provider.Creates;
        var workBefore = await WorkCount();
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 50)
                .Select(async _ =>
                {
                    using var scope = fixture.Host.Services.CreateScope();
                    return await scope
                        .ServiceProvider.GetRequiredService<HotelReservationService>()
                        .PurchaseAsync(owner, id, purchase, TestContext.Current.CancellationToken);
                })
        );
        results.ShouldAllBe(r => !r.IsError);
        (fixture.Provider.Creates - before).ShouldBe(1);
        results.Select(r => r.Value.AttemptId).Distinct().Count().ShouldBe(1);
        fixture.Capacity.Active.ShouldBe(0);
        (await WorkCount() - workBefore).ShouldBe(1);
        using var check = fixture.Host.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var retained = await db
            .Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        retained.AttemptId.ShouldNotBeNull();
        retained.ProtectedParty.ShouldNotBeNull().ShouldNotContain("fictional@example.invalid");
        (
            await check
                .ServiceProvider.GetRequiredService<HotelReservationService>()
                .DetailAsync(owner, id, TestContext.Current.CancellationToken)
        ).Value.Outcome.ShouldBe(ReservationOutcome.Confirmed);
    }

    [Fact]
    public async Task Retention_hides_details_at_deadline_and_physically_removes_content_without_reviving_claim()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await service.QuoteAsync(
                owner,
                id,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        var purchase = new HotelPurchase(
            Guid.NewGuid(),
            1,
            true,
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            )
        );
        (
            await service.PurchaseAsync(owner, id, purchase, TestContext.Current.CancellationToken)
        ).IsError.ShouldBeFalse();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var confirmed = (
            await service.DetailAsync(owner, id, TestContext.Current.CancellationToken)
        ).Value;
        var current = confirmed.Facts!;
        var different = current.Actual! with { Room = "Earlier mismatched room" };
        (
            await service.ApplyAsync(
                id,
                new(ObservationKind.Confirmed, current with { Actual = different }),
                false,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        var matched = await service.ApplyAsync(
            id,
            new(ObservationKind.Confirmed, current),
            false,
            TestContext.Current.CancellationToken
        );
        matched.Value.FirstMismatch!.Actual.Room.ShouldBe("Earlier mismatched room");
        var deadline = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db
            .Reservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.PrivatePurgeAt, deadline)
                        .SetProperty(r => r.Version, r => r.Version + 1),
                TestContext.Current.CancellationToken
            );
        var expired = (
            await service.DetailAsync(owner, id, TestContext.Current.CancellationToken)
        ).Value;
        expired.Accepted.ShouldBeNull();
        expired.Facts.ShouldBeNull();
        expired.FirstMismatch.ShouldBeNull();
        expired.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        var store = scope.ServiceProvider.GetRequiredService<HotelReservationStore>();
        (
            await store.PurgePrivateContentAsync(
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(1);
        var row = await db
            .Reservations.AsNoTracking()
            .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken);
        row.TermsJson.ShouldBeNull();
        row.ProtectedContext.ShouldBeNull();
        row.ProtectedParty.ShouldBeNull();
        row.AttemptId.ShouldBe(expired.AttemptId);
        row.ActionId.ShouldBe(purchase.ActionId);
        row.StateJson.ShouldNotContain("Fictional hotel");
        row.StateJson.ShouldNotContain("120.25");
        row.StateJson.ShouldNotContain("Earlier mismatched room");
        var before = fixture.Provider.Creates;
        (
            await service.PurchaseAsync(owner, id, purchase, TestContext.Current.CancellationToken)
        ).FirstError.ShouldBe(HotelErrors.Conflict);
        fixture.Provider.Creates.ShouldBe(before);
        (
            await store.PurgePrivateContentAsync(
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task Protected_content_is_bound_to_owner_revision_and_integrity()
    {
        var protector = fixture.Host.Services.GetRequiredService<IHotelPrivateProtector>();
        var binding = new HotelPrivacyBinding(Guid.NewGuid(), Guid.NewGuid(), 1, 1, "party");
        var secret = protector.Protect("fictional@example.invalid", binding);
        secret.IsError.ShouldBeFalse();
        secret.Value.ShouldNotContain("fictional@example.invalid");
        protector
            .Unprotect(secret.Value, binding with { OwnerId = Guid.NewGuid() })
            .IsError.ShouldBeTrue();
        protector
            .Unprotect(secret.Value, binding with { QuoteRevision = 2 })
            .IsError.ShouldBeTrue();
        protector.Unprotect(secret.Value[..^3] + "xxx", binding).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Key_outage_keeps_owned_HTTP_metadata_readable_and_purchase_fails_closed_before_claim()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await service.QuoteAsync(
                owner,
                id,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                ct
            )
        ).IsError.ShouldBeFalse();
        fixture.Protection.Offline = true;
        try
        {
            var decrypts = fixture.Protection.Decrypts;
            var protects = fixture.Protection.Protects;
            var creates = fixture.Provider.Creates;
            foreach (
                var path in new[] { $"/api/hotels/reservations/{id}", "/api/hotels/reservations" }
            )
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Add("X-Fictional-Owner", owner.ToString());
                using var result = await fixture.Client.SendAsync(request, ct);
                result.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
                result.Headers.CacheControl!.NoStore.ShouldBeTrue();
            }
            fixture.Protection.Decrypts.ShouldBe(decrypts);
            fixture.Protection.Protects.ShouldBe(protects);
            (
                await service.PurchaseAsync(
                    owner,
                    id,
                    new(
                        Guid.NewGuid(),
                        1,
                        true,
                        new(
                            [new("Fictional", "Traveller")],
                            "fictional@example.invalid",
                            "+44000000000",
                            true,
                            true
                        )
                    ),
                    ct
                )
            ).IsError.ShouldBeTrue();
            fixture.Provider.Creates.ShouldBe(creates);
            fixture.Capacity.Active.ShouldBe(0);
            (
                await scope
                    .ServiceProvider.GetRequiredService<HotelsDbContext>()
                    .Reservations.AsNoTracking()
                    .SingleAsync(r => r.Id == id, ct)
            ).AttemptId.ShouldBeNull();
        }
        finally
        {
            fixture.Protection.Offline = false;
        }
    }

    private async Task<long> WorkCount()
    {
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE message_type LIKE '%HotelWork%'";
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Exhausted_capacity_makes_zero_claims_then_replay_needs_no_permit()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var scope = fixture.Host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        (
            await service.QuoteAsync(
                owner,
                id,
                null,
                new(
                    DuffelStaysProviderTests.Request(),
                    new(
                        fixture.Provider.Key,
                        "Test",
                        "rate_fictional",
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        var purchase = new HotelPurchase(
            Guid.NewGuid(),
            1,
            true,
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            )
        );
        var before = fixture.Provider.Creates;
        fixture.Capacity.Available = false;
        try
        {
            (
                await service.PurchaseAsync(
                    owner,
                    id,
                    purchase,
                    TestContext.Current.CancellationToken
                )
            ).FirstError.ShouldBe(HotelErrors.Capacity);
            var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
            (
                await db
                    .Reservations.AsNoTracking()
                    .SingleAsync(r => r.Id == id, TestContext.Current.CancellationToken)
            ).AttemptId.ShouldBeNull();
            fixture.Provider.Creates.ShouldBe(before);
        }
        finally
        {
            fixture.Capacity.Available = true;
        }
        var result = await service.PurchaseAsync(
            owner,
            id,
            purchase,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        fixture.Capacity.Available = false;
        try
        {
            (
                await service.PurchaseAsync(
                    owner,
                    id,
                    purchase,
                    TestContext.Current.CancellationToken
                )
            ).Value.AttemptId.ShouldBe(result.Value.AttemptId);
        }
        finally
        {
            fixture.Capacity.Available = true;
        }
        fixture.Provider.Creates.ShouldBe(before + 1);
    }
}
