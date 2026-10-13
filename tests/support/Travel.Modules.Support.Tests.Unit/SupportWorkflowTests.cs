using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Tests.Unit;

public sealed class SupportWorkflowTests
{
    [Fact]
    public async Task Creation_saves_one_open_case_message_receipt_and_exact_text_without_order_lookup()
    {
        var context = new SupportTestContext();
        var result = await context.CreateAsync();
        result.IsError.ShouldBeFalse();
        result.Value.IsReplay.ShouldBeFalse();
        result.Value.Receipt.AppliedRevision.ShouldBe(1);
        result.Value.Receipt.AppliedState.ShouldBe(CaseState.Open);
        context.Store.Cases.Count.ShouldBe(1);
        context.Store.Messages.Count.ShouldBe(1);
        context.Store.Receipts.Count.ShouldBe(1);
        context.Orders.Calls.ShouldBe(0);
        var view = await context.Service.GetCaseAsync(
            context.Customer,
            context.CaseId,
            cancellationToken: TestContext.Current.CancellationToken
        );
        view.Value.Title.ShouldBe(" A literal <title> ");
        view.Value.Category.ShouldBe(ManualCategory.Unclassified);
        view.Value.Priority.ShouldBe(SupportPriority.Normal);
        view.Value.TriageSource.ShouldBe(TriageSource.Default);
        context.Store.Cases[context.CaseId].ProtectedTitle.ShouldNotContain("literal");
    }

    [Fact]
    public async Task Matching_creation_replays_receipt_before_rechecking_order()
    {
        var context = new SupportTestContext();
        var command = context.CreateCommand with { OrderId = context.OrderId };
        var first = await context.Service.CreateAsync(
            context.Customer,
            command,
            cancellationToken: TestContext.Current.CancellationToken
        );
        context.Orders.Unavailable = true;
        var replay = await context.Service.CreateAsync(
            context.Customer,
            command,
            cancellationToken: TestContext.Current.CancellationToken
        );
        replay.IsError.ShouldBeFalse();
        replay.Value.IsReplay.ShouldBeTrue();
        replay.Value.Receipt.ShouldBe(first.Value.Receipt);
        context.Orders.Calls.ShouldBe(1);
        var link = context.Store.Cases[context.CaseId].OrderLink;
        link.ShouldNotBeNull();
        link.OrderId.ShouldBe(context.OrderId);
        link.StreamVersion.ShouldBe(7);
        link.VerifiedAt.ShouldBe(context.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Changed_creation_text_message_or_link_conflicts_without_overwriting()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        foreach (
            var command in new[]
            {
                context.CreateCommand with
                {
                    Title = "changed",
                },
                context.CreateCommand with
                {
                    Body = "changed",
                },
                context.CreateCommand with
                {
                    MessageId = Guid.NewGuid(),
                },
                context.CreateCommand with
                {
                    OrderId = context.OrderId,
                },
            }
        )
        {
            (
                await context.Service.CreateAsync(
                    context.Customer,
                    command,
                    cancellationToken: TestContext.Current.CancellationToken
                )
            ).FirstError.Code.ShouldBe("Support.Conflict");
        }
        context.Store.Cases[context.CaseId].Revision.ShouldBe(1);
        context.Store.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Foreign_owner_and_invalid_operator_fail_before_decryption_or_receipt_disclosure()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var foreign = context.Customer with { Subject = Guid.NewGuid() };
        var invalidOperator = context.Operator with { AuthorizedParty = "travel-web" };
        context.Protector.UnprotectCalls = 0;
        (
            await context.Service.GetCaseAsync(
                foreign,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.GetReceiptAsync(
                foreign,
                context.CaseId,
                context.FirstMessageId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.ListMessagesAsync(
                invalidOperator,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Forbidden");
        (
            await context.Service.CreateAsync(
                context.Customer with
                {
                    IsAuthenticated = false,
                },
                context.CreateCommand,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Unauthenticated");
        context.Protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Reply_resolve_explicit_reopen_preserves_immutable_messages_and_bindings()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var firstMessage = context.Store.Messages[(context.CaseId, context.FirstMessageId)];
        var operatorReply = await context.ReplyAsync(
            context.Operator,
            1,
            SupportActionKind.OperatorReply
        );
        operatorReply.Value.Receipt.AppliedState.ShouldBe(CaseState.WaitingForCustomer);
        var customerReply = await context.ReplyAsync(
            context.Customer,
            2,
            SupportActionKind.CustomerReply
        );
        customerReply.Value.Receipt.AppliedState.ShouldBe(CaseState.Open);
        var resolve = await context.Service.ActAsync(
            context.Operator,
            new(context.CaseId, Guid.NewGuid(), 3, SupportActionKind.Resolve),
            cancellationToken: TestContext.Current.CancellationToken
        );
        resolve.Value.Receipt.AppliedState.ShouldBe(CaseState.Resolved);
        context.Store.Cases[context.CaseId].PurgeAt.ShouldBe(context.Clock.GetUtcNow().AddDays(90));
        (
            await context.ReplyAsync(context.Customer, 4, SupportActionKind.CustomerReply)
        ).FirstError.Code.ShouldBe("Support.Conflict");
        var reopen = await context.ReplyAsync(
            context.Customer,
            4,
            SupportActionKind.ReplyAndReopen
        );
        reopen.Value.Receipt.AppliedRevision.ShouldBe(5);
        context.Store.Cases[context.CaseId].PurgeAt.ShouldBeNull();
        context.Store.Cases[context.CaseId].ResolvedAt.ShouldBeNull();
        context.Store.Messages[(context.CaseId, context.FirstMessageId)].ShouldBe(firstMessage);
        var messages = await context.Service.ListMessagesAsync(
            context.Customer,
            context.CaseId,
            cancellationToken: TestContext.Current.CancellationToken
        );
        messages.IsError.ShouldBeFalse();
        messages.Value.Items[0].Body.ShouldBe(" A literal first message ");
        messages.Value.Items[0].WrittenAtRevision.ShouldBe(1);
    }

    [Fact]
    public async Task Retained_reply_replay_uses_written_revision_and_old_receipt_does_not_change_new_state()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var command = new ReplyToSupportCase(
            context.CaseId,
            Guid.NewGuid(),
            "exact original",
            1,
            SupportActionKind.CustomerReply
        );
        var first = await context.Service.ReplyAsync(
            context.Customer,
            command,
            cancellationToken: TestContext.Current.CancellationToken
        );
        await context.Service.ActAsync(
            context.Operator,
            new(context.CaseId, Guid.NewGuid(), 2, SupportActionKind.Resolve),
            cancellationToken: TestContext.Current.CancellationToken
        );
        var replay = await context.Service.ReplyAsync(
            context.Customer,
            command,
            cancellationToken: TestContext.Current.CancellationToken
        );
        replay.Value.IsReplay.ShouldBeTrue();
        replay.Value.Receipt.ShouldBe(first.Value.Receipt);
        context.Store.Cases[context.CaseId].Revision.ShouldBe(3);
        context.Store.Cases[context.CaseId].State.ShouldBe(CaseState.Resolved);
        (
            await context.Service.ReplyAsync(
                context.Customer,
                command with
                {
                    ExpectedRevision = 2,
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        (
            await context.Service.ReplyAsync(
                context.Customer,
                command with
                {
                    Body = "different",
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
    }

    [Fact]
    public async Task Common_action_namespace_blocks_reused_kind_or_actor_and_receipts_remain_actor_bound()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var action = new ActOnSupportCase(
            context.CaseId,
            Guid.NewGuid(),
            1,
            SupportActionKind.SetTriage,
            ManualCategory.Refund,
            SupportPriority.High
        );
        var triage = await context.Service.ActAsync(
            context.Operator,
            action,
            cancellationToken: TestContext.Current.CancellationToken
        );
        triage.Value.Receipt.Kind.ShouldBe(SupportActionKind.SetTriage);
        var saved = context.Store.Cases[context.CaseId];
        saved.Category.ShouldBe(ManualCategory.Refund);
        saved.Priority.ShouldBe(SupportPriority.High);
        saved.TriageSource.ShouldBe(TriageSource.Operator);
        var audit = context.Store.Receipts[(context.CaseId, action.ActionId)];
        audit.PreviousCategory.ShouldBe(ManualCategory.Unclassified);
        audit.PreviousPriority.ShouldBe(SupportPriority.Normal);
        (
            await context.Service.ActAsync(
                context.Operator,
                action with
                {
                    Kind = SupportActionKind.Resolve,
                    Category = null,
                    Priority = null,
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        (
            await context.Service.ReplyAsync(
                context.Operator,
                new(
                    context.CaseId,
                    action.ActionId,
                    "collision",
                    2,
                    SupportActionKind.OperatorReply
                ),
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        (
            await context.Service.ActAsync(
                context.Operator with
                {
                    Subject = Guid.NewGuid(),
                },
                action,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        context.Protector.UnprotectCalls = 0;
        (
            await context.Service.GetReceiptAsync(
                context.Customer,
                context.CaseId,
                action.ActionId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.GetReceiptAsync(
                context.Operator with
                {
                    Subject = Guid.NewGuid(),
                },
                context.CaseId,
                action.ActionId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.GetReceiptAsync(
                context.Operator,
                context.CaseId,
                action.ActionId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        context.Protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Expiry_at_deadline_conceals_reads_replays_and_reopen_without_decryption()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        await context.Service.ActAsync(
            context.Operator,
            new(context.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
            cancellationToken: TestContext.Current.CancellationToken
        );
        context.Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromTicks(1));
        (
            await context.Service.GetCaseAsync(
                context.Customer,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        context.Clock.Advance(TimeSpan.FromTicks(1));
        context.Protector.UnprotectCalls = 0;
        (
            await context.Service.GetCaseAsync(
                context.Customer,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (await context.CreateAsync()).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.ReplyAsync(context.Customer, 2, SupportActionKind.ReplyAndReopen)
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.GetReceiptAsync(
                context.Customer,
                context.CaseId,
                context.FirstMessageId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
        (
            await context.Service.ListCasesAsync(
                context.Customer,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).Value.Items.ShouldBeEmpty();
        context.Protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Protection_and_commit_failure_do_not_report_success_or_partial_data()
    {
        var context = new SupportTestContext();
        context.Protector.Unavailable = true;
        (await context.CreateAsync()).FirstError.Code.ShouldBe("Support.Unavailable");
        context.Store.Cases.ShouldBeEmpty();
        context.Protector.Unavailable = false;
        context.Store.FailWrites = true;
        (await context.CreateAsync()).FirstError.Code.ShouldBe("Support.Unavailable");
        context.Store.Cases.ShouldBeEmpty();
        context.Store.Messages.ShouldBeEmpty();
        context.Store.Receipts.ShouldBeEmpty();
        (
            await context.Service.GetReceiptAsync(
                context.Customer,
                context.CaseId,
                context.FirstMessageId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_unavailable_order_never_commits_a_linked_case(bool unavailable)
    {
        var context = new SupportTestContext();
        context.Orders.Unavailable = unavailable;
        context.Orders.Missing = !unavailable;
        var result = await context.Service.CreateAsync(
            context.Customer,
            context.CreateCommand with
            {
                OrderId = context.OrderId,
            },
            cancellationToken: TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe(
            unavailable ? "Support.OrderLinkUnavailable" : "Support.OrderLinkNotFound"
        );
        context.Store.Cases.ShouldBeEmpty();
        context.Store.WriteAttempts.ShouldBe(0);
        context.Protector.ProtectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_owner_order_proof_is_rejected_and_missing_operator_capability_cannot_write()
    {
        var context = new SupportTestContext();
        context.Orders.WrongOwner = true;
        (
            await context.Service.CreateAsync(
                context.Customer,
                context.CreateCommand with
                {
                    OrderId = context.OrderId,
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.OrderLinkNotFound");
        await context.CreateAsync();
        (
            await context.Service.ActAsync(
                context.Customer,
                new(context.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Forbidden");
        (
            await context.Service.ListCasesAsync(
                context.Operator with
                {
                    HasOperatorPermission = false,
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Forbidden");
        context.Store.Cases[context.CaseId].Revision.ShouldBe(1);
    }

    [Fact]
    public async Task Competing_reply_and_resolution_commit_only_one_revision_and_receipt()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        context.Store.SynchronizeNextTwoCaseReads();
        var results = await Task.WhenAll(
            context.ReplyAsync(context.Operator, 1, SupportActionKind.OperatorReply),
            context.Service.ActAsync(
                context.Operator,
                new(context.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        results.Count(result => !result.IsError).ShouldBe(1);
        results.Single(result => result.IsError).FirstError.Code.ShouldBe("Support.Conflict");
        context.Store.Cases[context.CaseId].Revision.ShouldBe(2);
        context.Store.Receipts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_transitions_enums_noop_triage_and_revision_overflow_do_not_mutate()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        (
            await context.ReplyAsync(context.Customer, 1, SupportActionKind.ReplyAndReopen)
        ).FirstError.Code.ShouldBe("Support.Conflict");
        (
            await context.ReplyAsync(context.Operator, 1, SupportActionKind.CustomerReply)
        ).FirstError.Code.ShouldBe("Support.Forbidden");
        (
            await context.Service.ActAsync(
                context.Operator,
                new(
                    context.CaseId,
                    Guid.NewGuid(),
                    1,
                    SupportActionKind.SetTriage,
                    (ManualCategory)999,
                    SupportPriority.Normal
                ),
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.InvalidInput");
        var triage = new ActOnSupportCase(
            context.CaseId,
            Guid.NewGuid(),
            1,
            SupportActionKind.SetTriage,
            ManualCategory.Unclassified,
            SupportPriority.Normal
        );
        (
            await context.Service.ActAsync(
                context.Operator,
                triage,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        (
            await context.Service.ActAsync(
                context.Operator,
                triage with
                {
                    ActionId = Guid.NewGuid(),
                    ExpectedRevision = 2,
                },
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        context.Store.Cases[context.CaseId] = context.Store.Cases[context.CaseId] with
        {
            Revision = int.MaxValue,
        };
        (
            await context.ReplyAsync(
                context.Customer,
                int.MaxValue,
                SupportActionKind.CustomerReply
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
    }

    [Fact]
    public async Task Message_keyset_continues_after_twenty_and_direct_receipt_needs_no_decryption()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        Guid last = Guid.Empty;
        for (var revision = 1; revision <= 21; revision++)
        {
            last = Guid.NewGuid();
            (
                await context.Service.ReplyAsync(
                    context.Customer,
                    new(
                        context.CaseId,
                        last,
                        $"message {revision}",
                        revision,
                        SupportActionKind.CustomerReply
                    ),
                    cancellationToken: TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeFalse();
        }
        var first = await context.Service.ListMessagesAsync(
            context.Customer,
            context.CaseId,
            cancellationToken: TestContext.Current.CancellationToken
        );
        first.Value.Items.Count.ShouldBe(20);
        first.Value.NextCursor.ShouldNotBeNull();
        var second = await context.Service.ListMessagesAsync(
            context.Customer,
            context.CaseId,
            first.Value.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken
        );
        second.Value.Items.Count.ShouldBe(2);
        second.Value.Items[1].MessageId.ShouldBe(last);
        second.Value.NextCursor.ShouldBeNull();
        first
            .Value.Items.Select(item => item.MessageId)
            .Intersect(second.Value.Items.Select(item => item.MessageId))
            .ShouldBeEmpty();
        context.Protector.Unavailable = true;
        var receipt = await context.Service.GetReceiptAsync(
            context.Customer,
            context.CaseId,
            last,
            cancellationToken: TestContext.Current.CancellationToken
        );
        receipt.IsError.ShouldBeFalse();
        receipt.Value.AppliedRevision.ShouldBe(22);
        (
            await context.Service.ListMessagesAsync(
                context.Customer,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Unavailable");
    }

    [Fact]
    public async Task Invalid_or_foreign_cursor_and_read_failure_are_not_empty_success()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        (
            await context.Service.ListMessagesAsync(
                context.Customer,
                context.CaseId,
                "garbage",
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.InvalidInput");
        (
            await context.Service.ListCasesAsync(
                context.Customer,
                cursor: "garbage",
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.InvalidInput");
        context.Store.FailReads = true;
        (
            await context.Service.ListCasesAsync(
                context.Operator,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Unavailable");
    }

    [Fact]
    public async Task Initial_receipt_equals_persisted_replay_with_database_microsecond_precision()
    {
        var context = new SupportTestContext();
        context.Store.NormalizeTimestamps = true;
        context.Clock.Advance(TimeSpan.FromTicks(7));
        var first = await context.CreateAsync();
        var replay = await context.CreateAsync();
        replay.Value.Receipt.ShouldBe(first.Value.Receipt);
        var resolve = new ActOnSupportCase(
            context.CaseId,
            Guid.NewGuid(),
            1,
            SupportActionKind.Resolve
        );
        var firstResolve = await context.Service.ActAsync(
            context.Operator,
            resolve,
            cancellationToken: TestContext.Current.CancellationToken
        );
        var replayResolve = await context.Service.ActAsync(
            context.Operator,
            resolve,
            cancellationToken: TestContext.Current.CancellationToken
        );
        replayResolve.Value.Receipt.ShouldBe(firstResolve.Value.Receipt);
    }

    [Fact]
    public async Task Linked_create_storage_failure_keeps_generic_uncertainty_error()
    {
        var context = new SupportTestContext();
        context.Store.FailWrites = true;
        var result = await context.Service.CreateAsync(
            context.Customer,
            context.CreateCommand with
            {
                OrderId = context.OrderId,
            },
            cancellationToken: TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Support.Unavailable");
        context.Store.WriteAttempts.ShouldBe(1);
        context.Store.Cases.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_identical_creates_return_one_retained_action()
    {
        var context = new SupportTestContext();
        context.Store.SynchronizeNextTwoCaseReads();
        var results = await Task.WhenAll(context.CreateAsync(), context.CreateAsync());
        results.ShouldAllBe(result => !result.IsError);
        results[0].Value.Receipt.ShouldBe(results[1].Value.Receipt);
        results.Count(result => result.Value.IsReplay).ShouldBe(1);
        context.Store.Cases.Count.ShouldBe(1);
        context.Store.Messages.Count.ShouldBe(1);
        context.Store.Receipts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Case_keyset_filters_owner_and_preserves_created_order_after_status_change()
    {
        var context = new SupportTestContext();
        for (var index = 0; index < 21; index++)
        {
            var command = context.CreateCommand with
            {
                CaseId = Guid.NewGuid(),
                MessageId = Guid.NewGuid(),
            };
            (
                await context.Service.CreateAsync(
                    context.Customer,
                    command,
                    cancellationToken: TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeFalse();
        }
        (
            await context.Service.CreateAsync(
                context.Customer with
                {
                    Subject = Guid.NewGuid(),
                },
                context.CreateCommand,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        context.Protector.UnprotectCalls = 0;
        var first = await context.Service.ListCasesAsync(
            context.Customer,
            cancellationToken: TestContext.Current.CancellationToken
        );
        first.Value.Items.Count.ShouldBe(20);
        context.Protector.UnprotectCalls.ShouldBe(20);
        first.Value.Items.ShouldAllBe(item => item.OwnerId == context.Customer.Subject);
        first.Value.NextCursor.ShouldNotBeNull();
        var updatedCaseId = first.Value.Items[0].CaseId;
        (
            await context.Service.ActAsync(
                context.Operator,
                new(updatedCaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeFalse();
        var continuation = await context.Service.ListCasesAsync(
            context.Customer,
            cursor: first.Value.NextCursor,
            cancellationToken: TestContext.Current.CancellationToken
        );
        continuation.Value.Items.Count.ShouldBe(1);
        continuation.Value.NextCursor.ShouldBeNull();
        first
            .Value.Items.Select(item => item.CaseId)
            .Intersect(continuation.Value.Items.Select(item => item.CaseId))
            .ShouldBeEmpty();
        (
            await context.Service.ListCasesAsync(
                context.Customer,
                new(CaseState.Resolved),
                first.Value.NextCursor,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.InvalidInput");
        (
            await context.Service.ListCasesAsync(
                context.Customer,
                new(CaseState.Resolved),
                cancellationToken: TestContext.Current.CancellationToken
            )
        )
            .Value.Items.Single()
            .CaseId.ShouldBe(updatedCaseId);
    }

    [Fact]
    public async Task Conversation_cursor_cannot_cross_case_and_old_operator_receipt_requires_current_capability()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        for (var revision = 1; revision <= 20; revision++)
            await context.ReplyAsync(context.Customer, revision, SupportActionKind.CustomerReply);
        var first = await context.Service.ListMessagesAsync(
            context.Customer,
            context.CaseId,
            cancellationToken: TestContext.Current.CancellationToken
        );
        var secondCase = context.CreateCommand with
        {
            CaseId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
        };
        await context.Service.CreateAsync(
            context.Customer,
            secondCase,
            cancellationToken: TestContext.Current.CancellationToken
        );
        context.Protector.UnprotectCalls = 0;
        (
            await context.Service.ListMessagesAsync(
                context.Customer,
                secondCase.CaseId,
                first.Value.NextCursor,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.InvalidInput");
        var action = new ActOnSupportCase(
            secondCase.CaseId,
            Guid.NewGuid(),
            1,
            SupportActionKind.Resolve
        );
        await context.Service.ActAsync(
            context.Operator,
            action,
            cancellationToken: TestContext.Current.CancellationToken
        );
        (
            await context.Service.GetReceiptAsync(
                context.Operator with
                {
                    HasOperatorPermission = false,
                },
                secondCase.CaseId,
                action.ActionId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Forbidden");
        context.Protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Control_replay_is_concealed_when_deadline_passes_during_receipt_read()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var action = new ActOnSupportCase(
            context.CaseId,
            Guid.NewGuid(),
            1,
            SupportActionKind.Resolve
        );
        await context.Service.ActAsync(
            context.Operator,
            action,
            cancellationToken: TestContext.Current.CancellationToken
        );
        context.Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromTicks(1));
        context.Store.AfterReceiptRead = () => context.Clock.Advance(TimeSpan.FromTicks(1));
        var replay = await context.Service.ActAsync(
            context.Operator,
            action,
            cancellationToken: TestContext.Current.CancellationToken
        );
        replay.IsError.ShouldBeTrue();
        replay.FirstError.Code.ShouldBe("Support.NotFound");
    }

    [Fact]
    public async Task Deadline_crossing_during_key_failure_stays_concealed()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        await context.Service.ActAsync(
            context.Operator,
            new(context.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
            cancellationToken: TestContext.Current.CancellationToken
        );
        context.Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromTicks(1));
        context.Protector.BeforeUnprotect = () =>
        {
            context.Clock.Advance(TimeSpan.FromTicks(1));
            context.Protector.Unavailable = true;
        };
        (
            await context.Service.GetCaseAsync(
                context.Customer,
                context.CaseId,
                cancellationToken: TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.NotFound");
    }
}
