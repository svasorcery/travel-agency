using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Travel.Modules.Support.Infrastructure.Persistence;
using Travel.Modules.Support.Infrastructure.Privacy;
using Travel.Modules.Support.Infrastructure.Retention;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Support.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SupportPersistenceTests : IntegrationTestBase
{
    private SupportTestRing ring = default!;
    private readonly FakeTimeProvider time = new(
        new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10 * 10, TimeSpan.Zero)
    );
    private readonly SupportCaller owner = new(Guid.NewGuid(), SupportActorKind.Customer, true);
    private readonly SupportCaller op = new(
        Guid.NewGuid(),
        SupportActorKind.Operator,
        true,
        true,
        "travel-support-operator"
    );
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override async ValueTask OnInitializedAsync()
    {
        ring = new();
        await using var db = Database();
        await db.Database.MigrateAsync();
    }

    protected override ValueTask OnDisposingAsync()
    {
        ring.Dispose();
        return ValueTask.CompletedTask;
    }

    private SupportDbContext Database()
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>().UseNpgsql(ConnectionString);
        SupportDbContextConfiguration.Configure(options);
        return new(options.Options);
    }

    private SupportCaseService Service(
        SupportDbContext db,
        ISupportTextProtector? protector = null
    ) =>
        new(new SupportCaseStore(db, time), protector ?? ring.Protector, new NoOrderAccess(), time);

    private async Task<(Guid CaseId, Guid MessageId)> Create()
    {
        var caseId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await using var db = Database();
        var result = await Service(db)
            .CreateAsync(
                owner,
                new(caseId, messageId, "Fictional title sentinel", "Fictional body sentinel"),
                Ct
            );
        result.IsError.ShouldBeFalse();
        result.Value.Receipt.AppliedRevision.ShouldBe(1);
        return (caseId, messageId);
    }

    [Fact]
    public async Task Partial_order_proof_cannot_pass_a_SQL_null_check()
    {
        var ids = await Create();
        await using var db = Database();
        var row = await db.Cases.SingleAsync(x => x.CaseId == ids.CaseId, Ct);
        row.OrderId = Guid.NewGuid();
        row.OrderVerifiedAt = time.GetUtcNow();
        row.OrderStreamVersion = null;
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Initial_and_replayed_receipt_dates_exactly_match_the_immutable_persisted_receipt()
    {
        time.SetUtcNow(time.GetUtcNow().AddTicks(7));
        var caseId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var command = new CreateSupportCase(
            caseId,
            messageId,
            "Fictional precision title",
            "Fictional precision body"
        );
        await using var first = Database();
        var initial = (await Service(first).CreateAsync(owner, command, Ct)).Value;
        await using var restart = Database();
        var persisted = (
            await new SupportCaseStore(restart, time).LoadReceiptAsync(caseId, messageId, Ct)
        ).Value!;
        initial.Receipt.AppliedAt.ShouldBe(persisted.AppliedAt);
        var replay = (await Service(restart).CreateAsync(owner, command, Ct)).Value;
        replay.IsReplay.ShouldBeTrue();
        replay.Receipt.ShouldBe(initial.Receipt);
    }

    [Fact]
    public async Task Ciphertext_is_persisted_and_restart_reads_text_while_receipt_needs_no_decryption()
    {
        var ids = await Create();
        await using (var inspect = Database())
        {
            var row = await inspect.Cases.AsNoTracking().SingleAsync(Ct);
            row.ProtectedTitle.ShouldNotContain("Fictional title sentinel");
            (await inspect.Messages.AsNoTracking().SingleAsync(Ct)).ProtectedBody.ShouldNotContain(
                "Fictional body sentinel"
            );
            (await inspect.Receipts.AsNoTracking().SingleAsync(Ct)).ActionId.ShouldBe(
                ids.MessageId
            );
        }
        using var restartProvider = ring.NewProvider();
        await using var restarted = Database();
        var read = await Service(restarted, new DataProtectionSupportTextProtector(restartProvider))
            .GetCaseAsync(owner, ids.CaseId, Ct);
        read.Value.Title.ShouldBe("Fictional title sentinel");
        read.Value.OrderId.ShouldBeNull();
        var receipt = await Service(restarted, new RejectDecryption())
            .GetReceiptAsync(owner, ids.CaseId, ids.MessageId, Ct);
        receipt.IsError.ShouldBeFalse();
        receipt.Value.AppliedRevision.ShouldBe(1);
    }

    [Fact]
    public async Task Persisted_title_and_messages_survive_restart_and_certificate_rotation_only_with_retained_read_key()
    {
        const string title = "Fictional title sentinel";
        const string firstBody = "Fictional body sentinel";
        const string rotatedBody = "Fictional rotated reply sentinel";
        var ids = await Create();
        var replyId = Guid.NewGuid();

        using (var restartedProvider = ring.NewProvider())
        {
            await using var restartedDb = Database();
            var service = Service(
                restartedDb,
                new DataProtectionSupportTextProtector(restartedProvider)
            );
            var detail = await service.GetCaseAsync(owner, ids.CaseId, Ct);
            detail.IsError.ShouldBeFalse();
            detail.Value.Title.ShouldBe(title);
            var messages = await service.ListMessagesAsync(
                owner,
                ids.CaseId,
                cancellationToken: Ct
            );
            messages.IsError.ShouldBeFalse();
            messages.Value.Items.Select(message => message.Body).ShouldBe([firstBody]);
        }

        ring.RotateCertificate(retainOld: true);
        using (var rotatedProvider = ring.NewProvider())
        {
            await using var rotatedDb = Database();
            var service = Service(
                rotatedDb,
                new DataProtectionSupportTextProtector(rotatedProvider)
            );
            var oldTitle = await service.GetCaseAsync(owner, ids.CaseId, Ct);
            oldTitle.IsError.ShouldBeFalse();
            oldTitle.Value.Title.ShouldBe(title);
            var oldMessages = await service.ListMessagesAsync(
                owner,
                ids.CaseId,
                cancellationToken: Ct
            );
            oldMessages.IsError.ShouldBeFalse();
            oldMessages.Value.Items.Select(message => message.Body).ShouldBe([firstBody]);
            var reply = await service.ReplyAsync(
                owner,
                new(ids.CaseId, replyId, rotatedBody, 1, SupportActionKind.CustomerReply),
                Ct
            );
            reply.IsError.ShouldBeFalse();
            reply.Value.Receipt.AppliedRevision.ShouldBe(2);
        }

        using (var freshProvider = ring.NewProvider())
        {
            await using var freshDb = Database();
            var service = Service(freshDb, new DataProtectionSupportTextProtector(freshProvider));
            var detail = await service.GetCaseAsync(owner, ids.CaseId, Ct);
            detail.IsError.ShouldBeFalse();
            detail.Value.Title.ShouldBe(title);
            var page = await service.ListMessagesAsync(owner, ids.CaseId, cancellationToken: Ct);
            page.IsError.ShouldBeFalse();
            page.Value.Items.Select(message => message.Body).ShouldBe([firstBody, rotatedBody]);
            page.Value.Items.Select(message => message.WrittenAtRevision).ShouldBe([1, 2]);
            page.Value.NextCursor.ShouldBeNull();
            var storedCase = await freshDb.Cases.AsNoTracking().SingleAsync(Ct);
            var storedMessages = await freshDb.Messages.AsNoTracking().ToListAsync(Ct);
            var storedReceipts = await freshDb.Receipts.AsNoTracking().ToListAsync(Ct);
            storedMessages.Count.ShouldBe(2);
            storedReceipts.Count.ShouldBe(2);
            foreach (var sentinel in new[] { title, firstBody, rotatedBody })
            {
                storedCase.ProtectedTitle.ShouldNotContain(sentinel);
                foreach (var message in storedMessages)
                    message.ProtectedBody.ShouldNotContain(sentinel);
                JsonSerializer.Serialize(storedReceipts).ShouldNotContain(sentinel);
            }
        }

        ring.Options.ReadCertificates.Clear();
        using var missingReaderProvider = ring.NewProvider();
        await using var missingReaderDb = Database();
        var missingReader = new DataProtectionSupportTextProtector(missingReaderProvider);
        var unavailableService = Service(missingReaderDb, missingReader);
        var unreadableTitle = await unavailableService.GetCaseAsync(owner, ids.CaseId, Ct);
        unreadableTitle.IsError.ShouldBeTrue();
        unreadableTitle.FirstError.Code.ShouldBe("Support.Unavailable");
        var unreadablePage = await unavailableService.ListMessagesAsync(
            owner,
            ids.CaseId,
            cancellationToken: Ct
        );
        unreadablePage.IsError.ShouldBeTrue();
        unreadablePage.FirstError.Code.ShouldBe("Support.Unavailable");
        var oldMessage = await missingReaderDb
            .Messages.AsNoTracking()
            .SingleAsync(message => message.MessageId == ids.MessageId, Ct);
        (
            await missingReader.UnprotectAsync(
                oldMessage.ProtectedBody,
                new(owner.Subject, ids.CaseId, ids.MessageId, oldMessage.WrittenAtRevision),
                Ct
            )
        ).IsError.ShouldBeTrue();
        var newMessage = await missingReaderDb
            .Messages.AsNoTracking()
            .SingleAsync(message => message.MessageId == replyId, Ct);
        var readableNewBody = await missingReader.UnprotectAsync(
            newMessage.ProtectedBody,
            new(owner.Subject, ids.CaseId, replyId, newMessage.WrittenAtRevision),
            Ct
        );
        readableNewBody.IsError.ShouldBeFalse();
        readableNewBody.Value.ShouldBe(rotatedBody);
        var metadataOnly = Service(missingReaderDb, new RejectDecryption());
        foreach (var actionId in new[] { ids.MessageId, replyId })
        {
            var receipt = await metadataOnly.GetReceiptAsync(owner, ids.CaseId, actionId, Ct);
            receipt.IsError.ShouldBeFalse();
            receipt.Value.ActionId.ShouldBe(actionId);
            receipt.Value.MessageId.ShouldBe(actionId);
            receipt.Value.AppliedRevision.ShouldBe(actionId == ids.MessageId ? 1 : 2);
        }
    }

    [Fact]
    public async Task Persisted_unreadable_title_or_body_returns_unavailable_instead_of_an_empty_page()
    {
        var ids = await Create();
        await using var db = Database();
        await db
            .Messages.Where(x => x.CaseId == ids.CaseId)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(x => x.ProtectedBody, "{\"Format\":1,\"Ciphertext\":\"broken\"}"),
                Ct
            );
        var page = await Service(db).ListMessagesAsync(owner, ids.CaseId, cancellationToken: Ct);
        page.IsError.ShouldBeTrue();
        page.FirstError.Code.ShouldBe("Support.Unavailable");
        await db
            .Cases.Where(x => x.CaseId == ids.CaseId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProtectedTitle, "broken"), Ct);
        (await Service(db).ListCasesAsync(owner, cancellationToken: Ct)).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_reply_and_resolve_commit_only_one_revision_and_its_receipt()
    {
        var ids = await Create();
        await using var left = Database();
        await using var right = Database();
        var results = await Task.WhenAll(
            Service(left)
                .ReplyAsync(
                    op,
                    new(
                        ids.CaseId,
                        Guid.NewGuid(),
                        "Fictional operator reply",
                        1,
                        SupportActionKind.OperatorReply
                    ),
                    Ct
                ),
            Service(right)
                .ActAsync(op, new(ids.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve), Ct)
        );
        results.Count(x => !x.IsError).ShouldBe(1);
        results.Single(x => x.IsError).FirstError.Code.ShouldBe("Support.Conflict");
        await using var inspect = Database();
        (await inspect.Cases.SingleAsync(Ct)).Revision.ShouldBe(2);
        (await inspect.Receipts.CountAsync(Ct)).ShouldBe(2);
        (await inspect.Messages.CountAsync(Ct)).ShouldBe(results[0].IsError ? 1 : 2);
    }

    [Fact]
    public async Task Common_mutation_namespace_collision_rolls_back_the_already_updated_case()
    {
        var ids = await Create();
        await using var db = Database();
        var store = new SupportCaseStore(db, time);
        var original = (await store.LoadCaseAsync(ids.CaseId, Ct)).Value!;
        var modified = original with
        {
            State = CaseState.Resolved,
            Revision = 2,
            UpdatedAt = time.GetUtcNow(),
            ResolvedAt = time.GetUtcNow(),
            PurgeAt = time.GetUtcNow().AddDays(90),
        };
        var collision = new SupportReceipt(
            ids.CaseId,
            ids.MessageId,
            SupportActionKind.Resolve,
            op.Subject,
            op.Kind,
            1,
            2,
            CaseState.Resolved,
            time.GetUtcNow(),
            null,
            null,
            null,
            null,
            null,
            null
        );
        var write = await store.TryUpdateAsync(1, modified, null, collision, Ct);
        write.IsError.ShouldBeTrue();
        write.FirstError.Code.ShouldBe("Support.Conflict");
        (await store.LoadCaseAsync(ids.CaseId, Ct)).Value!.Revision.ShouldBe(1);
        (await db.Messages.CountAsync(Ct)).ShouldBe(1);
        (await db.Receipts.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task More_than_twenty_messages_use_immutable_keyset_and_direct_actor_bound_receipt()
    {
        var ids = await Create();
        var actionId = Guid.Empty;
        for (var revision = 1; revision <= 24; revision++)
        {
            await using var db = Database();
            actionId = Guid.NewGuid();
            (
                await Service(db)
                    .ReplyAsync(
                        owner,
                        new(
                            ids.CaseId,
                            actionId,
                            "Fictional reply " + revision,
                            revision,
                            SupportActionKind.CustomerReply
                        ),
                        Ct
                    )
            ).IsError.ShouldBeFalse();
        }
        await using var read = Database();
        var first = (
            await Service(read).ListMessagesAsync(owner, ids.CaseId, cancellationToken: Ct)
        ).Value;
        first.Items.Count.ShouldBe(20);
        first.NextCursor.ShouldNotBeNull();
        var second = (
            await Service(read).ListMessagesAsync(owner, ids.CaseId, first.NextCursor, Ct)
        ).Value;
        second.Items.Count.ShouldBe(5);
        first
            .Items.Select(x => x.MessageId)
            .Intersect(second.Items.Select(x => x.MessageId))
            .ShouldBeEmpty();
        second.NextCursor.ShouldBeNull();
        (
            await Service(read, new RejectDecryption())
                .GetReceiptAsync(owner, ids.CaseId, actionId, Ct)
        ).Value.AppliedRevision.ShouldBe(25);
        (
            await Service(read)
                .GetReceiptAsync(owner with { Subject = Guid.NewGuid() }, ids.CaseId, actionId, Ct)
        ).FirstError.Code.ShouldBe("Support.NotFound");
    }

    [Fact]
    public async Task Expiry_conceals_reads_and_rejects_late_CAS_before_physical_metadata_only_purge()
    {
        var ids = await Create();
        await using var db = Database();
        var service = Service(db);
        (
            await service.ActAsync(
                op,
                new(ids.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                Ct
            )
        ).IsError.ShouldBeFalse();
        var resolved = (await new SupportCaseStore(db, time).LoadCaseAsync(ids.CaseId, Ct)).Value!;
        var runner = new SupportRetentionRunner(db, time);
        time.Advance(TimeSpan.FromDays(90) - TimeSpan.FromTicks(1));
        (await runner.RunPassAsync(Ct)).ShouldBe(0);
        (await service.GetCaseAsync(owner, ids.CaseId, Ct)).IsError.ShouldBeFalse();
        time.Advance(TimeSpan.FromTicks(1));
        (await service.GetCaseAsync(owner, ids.CaseId, Ct)).FirstError.Code.ShouldBe(
            "Support.NotFound"
        );
        (
            await service.GetReceiptAsync(owner, ids.CaseId, ids.MessageId, Ct)
        ).FirstError.Code.ShouldBe("Support.NotFound");
        var reopen = resolved with
        {
            State = CaseState.Open,
            Revision = 3,
            ResolvedAt = null,
            PurgeAt = null,
        };
        var receipt = new SupportReceipt(
            ids.CaseId,
            Guid.NewGuid(),
            SupportActionKind.ReplyAndReopen,
            owner.Subject,
            owner.Kind,
            2,
            3,
            CaseState.Open,
            time.GetUtcNow(),
            null,
            null,
            null,
            null,
            null,
            null
        );
        (
            await new SupportCaseStore(db, time).TryUpdateAsync(2, reopen, null, receipt, Ct)
        ).FirstError.Code.ShouldBe("Support.Conflict");
        (await runner.RunPassAsync(Ct)).ShouldBe(1);
        (await db.Cases.CountAsync(Ct)).ShouldBe(0);
        (await db.Messages.CountAsync(Ct)).ShouldBe(0);
        (await db.Receipts.CountAsync(Ct)).ShouldBe(0);
        (await runner.RunPassAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Retention_batch_and_pass_are_bounded_and_restart_finishes_remaining_unreadable_rows()
    {
        await using var db = Database();
        var now = time.GetUtcNow();
        db.Cases.AddRange(
            Enumerable
                .Range(0, 1005)
                .Select(_ => new SupportCaseEntity
                {
                    CaseId = Guid.NewGuid(),
                    OwnerId = owner.Subject,
                    FirstMessageId = Guid.NewGuid(),
                    ProtectedTitle =
                        "{\"Format\":1,\"Ciphertext\":\"unreadable-retention-fixture\"}",
                    TitleWrittenAtRevision = 1,
                    State = CaseState.Resolved,
                    Revision = 1,
                    CreatedAt = now.AddDays(-100),
                    UpdatedAt = now.AddDays(-90),
                    ResolvedAt = now.AddDays(-90),
                    PurgeAt = now,
                })
        );
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var runner = new SupportRetentionRunner(db, time);
        (await runner.ReadBatchAsync(Ct)).Count.ShouldBe(50);
        (await runner.RunPassAsync(Ct)).ShouldBe(1000);
        (await db.Cases.CountAsync(Ct)).ShouldBe(5);
        await using var restarted = Database();
        (await new SupportRetentionRunner(restarted, time).RunPassAsync(Ct)).ShouldBe(5);
        (await restarted.Cases.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Stale_delete_candidate_cannot_delete_a_reopened_case_and_other_worker_restart_is_safe()
    {
        var ids = await Create();
        await using var db = Database();
        var service = Service(db);
        (
            await service.ActAsync(
                op,
                new(ids.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                Ct
            )
        ).IsError.ShouldBeFalse();
        var row = (await new SupportCaseStore(db, time).LoadCaseAsync(ids.CaseId, Ct)).Value!;
        var candidate = new SupportPurgeCandidate(ids.CaseId, row.Revision, row.PurgeAt!.Value);
        time.Advance(TimeSpan.FromDays(89));
        (
            await service.ReplyAsync(
                owner,
                new(
                    ids.CaseId,
                    Guid.NewGuid(),
                    "Fictional reopened reply",
                    2,
                    SupportActionKind.ReplyAndReopen
                ),
                Ct
            )
        ).IsError.ShouldBeFalse();
        time.Advance(TimeSpan.FromDays(2));
        (await new SupportRetentionRunner(db, time).DeleteAsync(candidate, Ct)).ShouldBeFalse();
        (await db.Cases.CountAsync(Ct)).ShouldBe(1);
        (
            await service.ActAsync(
                op,
                new(ids.CaseId, Guid.NewGuid(), 3, SupportActionKind.Resolve),
                Ct
            )
        ).IsError.ShouldBeFalse();
        time.Advance(TimeSpan.FromDays(90));
        await using var other = Database();
        var batches = await Task.WhenAll(
            new SupportRetentionRunner(db, time).RunPassAsync(Ct),
            new SupportRetentionRunner(other, time).RunPassAsync(Ct)
        );
        batches.Sum().ShouldBe(1);
        await using var restart = Database();
        (await new SupportRetentionRunner(restart, time).RunPassAsync(Ct)).ShouldBe(0);
    }

    private sealed class NoOrderAccess : IFlightsSupportOrderAccess
    {
        public Task<ErrorOr<FlightOrderOwnershipProof>> VerifyOwnershipAsync(
            Guid ownerId,
            Guid orderId,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Unlinked support cases must not query Flights.");
    }

    private sealed class RejectDecryption : ISupportTextProtector
    {
        public Task<ErrorOr<string>> ProtectAsync(
            string plaintext,
            SupportTextBinding binding,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Unexpected protection.");

        public Task<ErrorOr<string>> UnprotectAsync(
            string envelope,
            SupportTextBinding binding,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Receipt must not decrypt.");
    }
}
