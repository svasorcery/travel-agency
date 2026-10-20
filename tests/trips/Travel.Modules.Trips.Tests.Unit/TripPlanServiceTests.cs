using System.Text;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Xunit;
using static Travel.Modules.Trips.Tests.Unit.TripPlanTests;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class TripPlanServiceTests
{
    [Fact]
    public async Task Trusted_owner_is_required_before_any_payload_access()
    {
        OwnerIdentity.Create("", Guid.NewGuid()).IsError.ShouldBeTrue();
        OwnerIdentity.Create("https://issuer.test", Guid.Empty).IsError.ShouldBeTrue();
        var f = new Fixture();
        (await f.Service.GetAsync(null, Guid.NewGuid())).FirstError.Code.ShouldBe(
            "Trips.Forbidden"
        );
        (await f.Service.ListAsync(null)).FirstError.Code.ShouldBe("Trips.Forbidden");
        (await f.Service.CreateAsync(null, new("План", Brief()))).FirstError.Code.ShouldBe(
            "Trips.Forbidden"
        );
        (
            await f.Service.ReplaceAsync(null, Guid.NewGuid(), 1, new("План", Brief(), []))
        ).FirstError.Code.ShouldBe("Trips.Forbidden");
        (await f.Service.DeleteAsync(null, Guid.NewGuid(), 1)).FirstError.Code.ShouldBe(
            "Trips.Forbidden"
        );
        f.Store.Calls.ShouldBe(0);
        f.Protector.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Foreign_row_is_indistinguishable_from_unknown_before_decrypt_even_if_store_is_faulty()
    {
        var f = new Fixture();
        var plan = await f.Create();
        var other = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        f.Store.ReturnForeignRow = true;
        (await f.Service.GetAsync(other, plan.TripId)).FirstError.Code.ShouldBe("Trips.NotFound");
        (
            await f.Service.ReplaceAsync(other, plan.TripId, 1, new("Иное", Brief(), Days(plan)))
        ).FirstError.Code.ShouldBe("Trips.NotFound");
        (await f.Service.GetAsync(Owner, Guid.NewGuid())).FirstError.Code.ShouldBe(
            "Trips.NotFound"
        );
        f.Protector.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Create_owns_identity_revision_empty_days_and_microsecond_timestamps()
    {
        var f = new Fixture();
        f.Clock.Now = Now.AddTicks(7);
        var plan = await f.Create();
        plan.TripId.ShouldNotBe(Guid.Empty);
        plan.Revision.ShouldBe(1);
        plan.CreatedAt.ShouldBe(Now);
        plan.UpdatedAt.ShouldBe(Now);
        f.Codec.Reserved.ShouldBeTrue();
        f.Store.LastCapacity.ShouldBe(100);
        f.Store.Rows.Single().Value.ProtectedPayload.ShouldStartWith("protected:");
        var read = (await f.Service.GetAsync(Owner, plan.TripId)).Value;
        read.CreatedAt.ShouldBe(plan.CreatedAt);
        f.Store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_create_does_not_protect_or_store()
    {
        var f = new Fixture();
        (await f.Service.CreateAsync(Owner, new("bad\n", Brief()))).IsError.ShouldBeTrue();
        f.Store.Calls.ShouldBe(0);
        f.Protector.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task Owner_capacity_is_an_atomic_store_outcome_without_retry()
    {
        var f = new Fixture();
        f.Store.CreateError = TripErrors.CapacityExceeded;
        (await f.Service.CreateAsync(Owner, new("План", Brief()))).FirstError.Code.ShouldBe(
            "Trips.CapacityExceeded"
        );
        f.Store.Writes.ShouldBe(1);
        f.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stale_or_invalid_replacement_is_atomic_and_does_not_protect_again()
    {
        var f = new Fixture();
        var plan = await f.Create();
        (
            await f.Service.ReplaceAsync(Owner, plan.TripId, 2, new("Иное", Brief(), Days(plan)))
        ).FirstError.Code.ShouldBe("Trips.RevisionMismatch");
        (
            await f.Service.ReplaceAsync(Owner, plan.TripId, 1, new("Иное", Brief(), []))
        ).FirstError.Code.ShouldBe("Trips.InvalidInput");
        f.Protector.Writes.ShouldBe(1);
        f.Store.Writes.ShouldBe(1);
        (await f.Service.GetAsync(Owner, plan.TripId)).Value.Title.ShouldBe("План");
    }

    [Fact]
    public async Task No_op_keeps_original_envelope_revision_and_timestamp_and_still_checks_CAS()
    {
        var f = new Fixture();
        var plan = await f.Create();
        var row = f.Store.Rows[plan.TripId];
        f.Clock.Now = Now.AddDays(1);
        var same = (
            await f.Service.ReplaceAsync(
                Owner,
                plan.TripId,
                1,
                new(plan.Title, plan.Brief.ToInput(), Days(plan))
            )
        ).Value;
        same.Revision.ShouldBe(1);
        same.UpdatedAt.ShouldBe(Now);
        f.Store.Rows[plan.TripId].ShouldBeSameAs(row);
        f.Protector.Writes.ShouldBe(1);
        f.Store.ReplaceError = TripErrors.RevisionMismatch;
        (
            await f.Service.ReplaceAsync(
                Owner,
                plan.TripId,
                1,
                new(plan.Title, plan.Brief.ToInput(), Days(plan))
            )
        ).FirstError.Code.ShouldBe("Trips.RevisionMismatch");
    }

    [Fact]
    public async Task Effective_change_uses_microseconds_and_lost_CAS_does_not_overwrite()
    {
        var f = new Fixture();
        var plan = await f.Create();
        f.Clock.Now = Now.AddMinutes(1).AddTicks(9);
        var changed = (
            await f.Service.ReplaceAsync(Owner, plan.TripId, 1, new("Иное", Brief(), Days(plan)))
        ).Value;
        changed.Revision.ShouldBe(2);
        changed.UpdatedAt.ShouldBe(Now.AddMinutes(1));
        f.Store.ReplaceError = TripErrors.RevisionMismatch;
        (
            await f.Service.ReplaceAsync(
                Owner,
                plan.TripId,
                2,
                new("Проигравший", Brief(), Days(changed))
            )
        ).FirstError.Code.ShouldBe("Trips.RevisionMismatch");
        (await f.Service.GetAsync(Owner, plan.TripId)).Value.Title.ShouldBe("Иное");
    }

    [Fact]
    public async Task Delete_wins_against_inflight_edit_without_upsert_and_needs_no_decryption()
    {
        var f = new Fixture();
        var plan = await f.Create();
        f.Store.BeforeReplace = () => f.Store.Rows.Remove(plan.TripId);
        (
            await f.Service.ReplaceAsync(Owner, plan.TripId, 1, new("Иное", Brief(), Days(plan)))
        ).FirstError.Code.ShouldBe("Trips.NotFound");
        (await f.Service.GetAsync(Owner, plan.TripId)).FirstError.Code.ShouldBe("Trips.NotFound");
        var another = await f.Create();
        var reads = f.Protector.Reads;
        (await f.Service.DeleteAsync(Owner, another.TripId, 2)).FirstError.Code.ShouldBe(
            "Trips.RevisionMismatch"
        );
        (await f.Service.DeleteAsync(Owner, another.TripId, 1)).IsError.ShouldBeFalse();
        f.Protector.Reads.ShouldBe(reads);
    }

    [Fact]
    public async Task Complete_detail_size_with_reserve_rejects_before_protection_or_commit()
    {
        var f = new Fixture();
        var plan = await f.Create();
        f.Codec.ReservedSize = TripPlanService.MaximumBodyBytes + 1;
        (
            await f.Service.ReplaceAsync(Owner, plan.TripId, 1, new("Иное", Brief(), Days(plan)))
        ).FirstError.Code.ShouldBe("Trips.PlanTooLarge");
        f.Protector.Writes.ShouldBe(1);
        f.Store.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task Maximum_reserve_and_multibyte_bytes_are_checked_before_create_commit()
    {
        var f = new Fixture();
        f.Codec.ReservedSize = TripPlanService.MaximumBodyBytes + 1;
        (await f.Service.CreateAsync(Owner, new("😀", Brief()))).FirstError.Code.ShouldBe(
            "Trips.PlanTooLarge"
        );
        f.Protector.Writes.ShouldBe(0);
        f.Store.Writes.ShouldBe(0);
        f.Codec.Reserved.ShouldBeTrue();
    }

    [Fact]
    public async Task Accepted_near_limit_payload_remains_readable_and_oversize_is_all_or_nothing()
    {
        var f = new Fixture();
        var plan = await f.Create(14);
        TripDayInput[] SizedDays(int scalars) =>
            plan
                .Days.Select(day => new TripDayInput(
                    day.Date,
                    Enumerable
                        .Range(0, 8)
                        .Select(_ => Block() with { Note = new string('я', scalars) })
                        .ToArray()
                ))
                .ToArray();
        // Default JSON escaping uses six bytes for each Cyrillic scalar. Find a real byte boundary, not a character count.
        var count = 1;
        for (; count < 500; count++)
        {
            var candidate = plan.Replace(
                plan.Title,
                plan.Brief.ToInput(),
                SizedDays(count),
                Now
            ).Value;
            if (
                f.Codec.MeasureDetailBytes(candidate, true).Value > TripPlanService.MaximumBodyBytes
            )
                break;
        }
        count.ShouldBeLessThan(500);
        var valid = await f.Service.ReplaceAsync(
            Owner,
            plan.TripId,
            1,
            new(plan.Title, plan.Brief.ToInput(), SizedDays(count - 1))
        );
        valid.IsError.ShouldBeFalse();
        (await f.Service.GetAsync(Owner, plan.TripId)).Value.Revision.ShouldBe(2);
        var writes = f.Store.Writes;
        var tooLarge = await f.Service.ReplaceAsync(
            Owner,
            plan.TripId,
            2,
            new(plan.Title, plan.Brief.ToInput(), SizedDays(count))
        );
        tooLarge.FirstError.Code.ShouldBe("Trips.PlanTooLarge");
        f.Store.Writes.ShouldBe(writes);
        (await f.Service.GetAsync(Owner, plan.TripId)).Value.Revision.ShouldBe(2);
    }

    [Fact]
    public async Task Empty_and_unavailable_list_remain_distinct_and_protection_fails_closed()
    {
        var f = new Fixture();
        (await f.Service.ListAsync(Owner)).Value.Items.ShouldBeEmpty();
        f.Store.ReadError = TripErrors.Unavailable;
        (await f.Service.ListAsync(Owner)).FirstError.Code.ShouldBe("Trips.Unavailable");
        f.Store.ReadError = null;
        var plan = await f.Create();
        f.Protector.FailReads = true;
        (await f.Service.GetAsync(Owner, plan.TripId)).FirstError.Code.ShouldBe(
            "Trips.Unavailable"
        );
        (await f.Service.ListAsync(Owner)).FirstError.Code.ShouldBe("Trips.Unavailable");
        f.Protector.FailWrites = true;
        (await f.Service.CreateAsync(Owner, new("План", Brief()))).FirstError.Code.ShouldBe(
            "Trips.Unavailable"
        );
    }

    [Fact]
    public async Task Cursor_is_bounded_owner_scoped_and_validated_before_store()
    {
        var f = new Fixture();
        (await f.Service.ListAsync(Owner, "garbage")).FirstError.Code.ShouldBe(
            "Trips.InvalidCursor"
        );
        (await f.Service.ListAsync(Owner, new string('a', 257))).FirstError.Code.ShouldBe(
            "Trips.InvalidCursor"
        );
        f.Store.Calls.ShouldBe(0);
        for (var i = 0; i < 21; i++)
            await f.Create();
        var page = (await f.Service.ListAsync(Owner)).Value;
        page.Items.Count.ShouldBe(20);
        page.NextCursor.ShouldNotBeNull();
        f.Store.LastLimit.ShouldBe(20);
        page.NextCursor.ShouldNotContain("План");
        var rest = (await f.Service.ListAsync(Owner, page.NextCursor)).Value;
        rest.Items.Count.ShouldBe(1);
        rest.Items.Select(x => x.TripId)
            .Intersect(page.Items.Select(x => x.TripId))
            .ShouldBeEmpty();
        var other = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        (await f.Service.ListAsync(other, page.NextCursor)).FirstError.Code.ShouldBe(
            "Trips.InvalidCursor"
        );
        typeof(TripPlanSummary).GetProperty("Days").ShouldBeNull();
        typeof(TripPlanSummary).GetProperty("TotalBudget").ShouldBeNull();
    }

    [Fact]
    public async Task Uncertain_create_is_never_retried_even_if_store_committed_before_timeout()
    {
        var f = new Fixture();
        f.Store.LoseCreateResponse = true;
        await Should.ThrowAsync<TimeoutException>(async () =>
            await f.Service.CreateAsync(Owner, new("План", Brief()))
        );
        f.Store.Writes.ShouldBe(1);
        f.Store.Rows.Count.ShouldBe(1);
        (await f.Service.ListAsync(Owner)).Value.Items.Count.ShouldBe(1);
    }

    private sealed class Fixture
    {
        public Store Store { get; } = new();
        public Protector Protector { get; } = new();
        public Codec Codec { get; } = new();
        public Clock Clock { get; } = new();
        public CancellableService Service { get; }

        public Fixture() => Service = new(new(Store, Protector, Codec, new Cursor(), Clock));

        public async Task<TripPlan> Create(int days = 2) =>
            (await Service.CreateAsync(Owner, new("План", Brief(days)))).Value;
    }

    private sealed class CancellableService(TripPlanService service)
    {
        public Task<ErrorOr<TripPlan>> GetAsync(OwnerIdentity? owner, Guid id) =>
            service.GetAsync(owner, id, TestContext.Current.CancellationToken);

        public Task<ErrorOr<TripPlan>> CreateAsync(OwnerIdentity? owner, CreateTripPlan command) =>
            service.CreateAsync(owner, command, TestContext.Current.CancellationToken);

        public Task<ErrorOr<TripPlan>> ReplaceAsync(
            OwnerIdentity? owner,
            Guid id,
            long revision,
            ReplaceTripPlan command
        ) =>
            service.ReplaceAsync(
                owner,
                id,
                revision,
                command,
                TestContext.Current.CancellationToken
            );

        public Task<ErrorOr<Success>> DeleteAsync(OwnerIdentity? owner, Guid id, long revision) =>
            service.DeleteAsync(owner, id, revision, TestContext.Current.CancellationToken);

        public Task<ErrorOr<TripPlanPage>> ListAsync(OwnerIdentity? owner, string? cursor = null) =>
            service.ListAsync(owner, cursor, TestContext.Current.CancellationToken);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = TripPlanTests.Now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store : ITripPlanStore
    {
        public Dictionary<Guid, TripPlanRecord> Rows { get; } = [];
        public int Calls { get; private set; }
        public int Writes { get; private set; }
        public int LastLimit { get; private set; }
        public int LastCapacity { get; private set; }
        public bool ReturnForeignRow { get; set; }
        public bool LoseCreateResponse { get; set; }
        public Error? CreateError { get; set; }
        public Error? ReplaceError { get; set; }
        public Error? ReadError { get; set; }
        public Action? BeforeReplace { get; set; }

        public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid tripId,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (ReadError is { } error)
                return Task.FromResult<ErrorOr<TripPlanRecord?>>(error);
            Rows.TryGetValue(tripId, out var row);
            return Task.FromResult<ErrorOr<TripPlanRecord?>>(
                row is not null && (row.Owner == owner || ReturnForeignRow) ? row : null
            );
        }

        public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
            OwnerIdentity owner,
            TripPlanPosition? after,
            int limit,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            LastLimit = limit;
            if (ReadError is { } error)
                return Task.FromResult<ErrorOr<TripPlanRecordPage>>(error);
            var rows = Rows
                .Values.Where(row =>
                    row.Owner == owner
                    && (
                        after is null
                        || row.UpdatedAt < after.UpdatedAt
                        || (
                            row.UpdatedAt == after.UpdatedAt
                            && row.TripId.CompareTo(after.TripId) > 0
                        )
                    )
                )
                .OrderByDescending(row => row.UpdatedAt)
                .ThenBy(row => row.TripId)
                .ToArray();
            return Task.FromResult<ErrorOr<TripPlanRecordPage>>(
                new TripPlanRecordPage(rows.Take(limit).ToArray(), rows.Length > limit)
            );
        }

        public Task<ErrorOr<Success>> TryCreateAsync(
            TripPlanRecord record,
            int ownerCapacity,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Writes++;
            LastCapacity = ownerCapacity;
            if (CreateError is { } error)
                return Task.FromResult<ErrorOr<Success>>(error);
            Rows.Add(record.TripId, record);
            if (LoseCreateResponse)
                throw new TimeoutException();
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<Success>> TryReplaceAsync(
            long expectedRevision,
            TripPlanRecord record,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Writes++;
            BeforeReplace?.Invoke();
            if (ReplaceError is { } error)
                return Task.FromResult<ErrorOr<Success>>(error);
            if (!Rows.TryGetValue(record.TripId, out var row) || row.Owner != record.Owner)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.NotFound);
            if (row.Revision != expectedRevision)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            Rows[record.TripId] = record;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<Success>> TryDeleteAsync(
            OwnerIdentity owner,
            Guid tripId,
            long expectedRevision,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Writes++;
            if (!Rows.TryGetValue(tripId, out var row) || row.Owner != owner)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.NotFound);
            if (row.Revision != expectedRevision)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            Rows.Remove(tripId);
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    private sealed class Protector : ITripPlanProtector
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }

        public Task<ErrorOr<string>> ProtectAsync(
            string plaintext,
            TripPlanBinding binding,
            CancellationToken cancellationToken
        )
        {
            Writes++;
            return Task.FromResult<ErrorOr<string>>(
                FailWrites ? TripErrors.Unavailable : "protected:" + plaintext
            );
        }

        public Task<ErrorOr<string>> UnprotectAsync(
            string envelope,
            TripPlanBinding binding,
            CancellationToken cancellationToken
        )
        {
            Reads++;
            return Task.FromResult<ErrorOr<string>>(
                FailReads ? TripErrors.Unavailable : envelope[10..]
            );
        }
    }

    // This fake tests application orchestration and real UTF-8 byte boundaries. Infrastructure tests own the production canonical codec/protection proof.
    private sealed class Codec : ITripPlanCodec
    {
        private readonly Dictionary<string, TripPlan> snapshots = [];
        public bool Reserved { get; private set; }
        public int? ReservedSize { get; set; }

        private static string Json(TripPlan plan, bool reserve) =>
            JsonSerializer.Serialize(
                new
                {
                    plan.TripId,
                    Revision = (reserve ? long.MaxValue : plan.Revision).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    plan.Title,
                    plan.Brief,
                    plan.Destination,
                    plan.Days,
                    plan.CreatedAt,
                    plan.UpdatedAt,
                    plan.KnownUserEstimateSubtotal,
                    plan.UnknownEstimateCount,
                    plan.Warnings,
                }
            );

        public ErrorOr<string> Serialize(TripPlan plan)
        {
            var json = Json(plan, false);
            snapshots[json] = plan;
            return json;
        }

        public ErrorOr<TripPlan> Deserialize(string plaintext, TripPlanRecord record) =>
            snapshots.TryGetValue(plaintext, out var plan) ? plan : TripErrors.Unavailable;

        public ErrorOr<int> MeasureDetailBytes(TripPlan plan, bool reserveMaximumServerFields)
        {
            Reserved |= reserveMaximumServerFields;
            return (reserveMaximumServerFields ? ReservedSize : null)
                ?? Encoding.UTF8.GetByteCount(Json(plan, reserveMaximumServerFields));
        }

        public ErrorOr<int> MeasurePageBytes(TripPlanPage page) =>
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(page));

        public ErrorOr<string> SerializePage(TripPlanPage page) => JsonSerializer.Serialize(page);
    }

    private sealed class Cursor : ITripPlanCursor
    {
        private readonly Dictionary<
            string,
            (OwnerIdentity Owner, TripPlanPosition Position)
        > positions = [];

        public ErrorOr<string> Encode(OwnerIdentity owner, TripPlanPosition position)
        {
            var token = Guid.NewGuid().ToString("N");
            positions[token] = (owner, position);
            return token;
        }

        public ErrorOr<TripPlanPosition> Decode(OwnerIdentity owner, string cursor) =>
            positions.TryGetValue(cursor, out var value) && value.Owner == owner
                ? value.Position
                : TripErrors.InvalidCursor;
    }
}
