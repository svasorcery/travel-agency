using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.SavedTravelers;

public sealed class SavedTravelerServiceTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly SavedTravelerId Id = new(Guid.NewGuid());
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly SavedTravelerDetails Details = SavedTravelerDetails
        .CreateRaw(
            "mr",
            "Fiction",
            "Traveler",
            new(2009, 10, 3),
            "male",
            "fiction@example.test",
            "+12025550123",
            new(2026, 10, 3)
        )
        .Value;
    private static readonly ProtectedSavedTravelerSnapshot Envelope = ProtectedSavedTravelerSnapshot
        .Create(1, "ZW5jcnlwdGVk")
        .Value;
    private readonly List<string> _calls = [];
    private readonly FakeStore _store;
    private readonly FakeProtector _protector;
    private readonly SavedTravelerService _service;

    public SavedTravelerServiceTests()
    {
        _store = new(_calls);
        _protector = new(_calls);
        _service = new(_store, _protector, new FakeTimeProvider(Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_foreign_update_stops_before_crypto(bool foreign)
    {
        _store.Record = foreign ? Record() with { OwnerUserId = Guid.NewGuid() } : null;
        var result = await _service.UpdateAsync(
            Owner,
            Id,
            Guid.NewGuid(),
            Details,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Flights.TravelerNotFound");
        _calls.ShouldBe(["get"]);
    }

    [Fact]
    public async Task Stale_update_stops_before_crypto()
    {
        _store.Record = Record();
        var result = await _service.UpdateAsync(
            Owner,
            Id,
            Guid.NewGuid(),
            Details,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Flights.TravelerPreconditionFailed");
        _calls.ShouldBe(["get"]);
    }

    [Theory]
    [InlineData(SavedTravelerVisibility.Owned, "Flights.TravelerPreconditionFailed")]
    [InlineData(SavedTravelerVisibility.Foreign, "Flights.TravelerNotFound")]
    public async Task Create_collision_checks_visibility_before_crypto(
        SavedTravelerVisibility visibility,
        string code
    )
    {
        _store.Visibility = visibility;
        (
            await _service.CreateAsync(Owner, Id, Details, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe(code);
        _calls.ShouldBe(["visibility"]);
    }

    [Fact]
    public async Task Create_stores_ciphertext_with_fresh_bound_revision_and_returns_only_receipt()
    {
        var result = await _service.CreateAsync(
            Owner,
            Id,
            Details,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        _calls.ShouldBe(["visibility", "protect", "create"]);
        var written = _store.Written!;
        written.Id.ShouldBe(Id);
        written.OwnerUserId.ShouldBe(Owner);
        written.CreatedAt.ShouldBe(Now);
        written.UpdatedAt.ShouldBe(Now);
        written.Revision.ShouldNotBe(Guid.Empty);
        written.ProtectedDetails.ShouldBe(Envelope);
        _protector.Context.ShouldBe(new(Owner, Id.Value, written.Revision));
        result.Value.ShouldBe(new(Id, written.Revision));
        JsonSerializer.Serialize(result.Value).ShouldNotContain("Fiction");
        JsonSerializer.Serialize(written).ShouldNotContain("fiction@example.test");
    }

    [Fact]
    public async Task Update_preserves_created_time_and_uses_atomic_expected_revision()
    {
        _store.Record = Record() with { CreatedAt = Now.AddDays(-1) };
        var expected = _store.Record.Revision;
        var result = await _service.UpdateAsync(
            Owner,
            Id,
            expected,
            Details,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        _calls.ShouldBe(["get", "protect", "update"]);
        _store.Expected.ShouldBe(expected);
        _store.Written!.Revision.ShouldNotBe(expected);
        _store.Written.CreatedAt.ShouldBe(Now.AddDays(-1));
        _store.Written.UpdatedAt.ShouldBe(Now);
        _protector.Context.ShouldBe(new(Owner, Id.Value, result.Value.Revision));
    }

    [Theory]
    [InlineData(SavedTravelerWriteOutcome.NotFound, "Flights.TravelerNotFound")]
    [InlineData(SavedTravelerWriteOutcome.PreconditionFailed, "Flights.TravelerPreconditionFailed")]
    public async Task Atomic_write_failure_returns_no_receipt(
        SavedTravelerWriteOutcome outcome,
        string code
    )
    {
        _store.WriteOutcome = outcome;
        (
            await _service.CreateAsync(Owner, Id, Details, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe(code);
    }

    [Fact]
    public async Task Delete_never_requires_protection_or_decryption()
    {
        _protector.Unavailable = true;
        var expected = Guid.NewGuid();
        (
            await _service.DeleteAsync(Owner, Id, expected, TestContext.Current.CancellationToken)
        ).IsError.ShouldBeFalse();
        _calls.ShouldBe(["delete"]);
        _store.Expected.ShouldBe(expected);
    }

    [Fact]
    public async Task Foreign_read_never_decrypts()
    {
        _store.Record = Record() with { OwnerUserId = Guid.NewGuid() };
        (
            await _service.GetAsync(Owner, Id, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.TravelerNotFound");
        _calls.ShouldBe(["get"]);
    }

    [Fact]
    public async Task List_reads_lookahead_but_decrypts_only_twenty()
    {
        _store.Rows = Enumerable.Range(0, 21).Select(_ => Record(new(Guid.NewGuid()))).ToArray();
        var result = await _service.ListAsync(Owner, 20, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Items.Count.ShouldBe(20);
        result.Value.Offset.ShouldBe(20);
        result.Value.HasMore.ShouldBeTrue();
        _store.Offset.ShouldBe(20);
        _calls.Count(x => x == "unprotect").ShouldBe(20);
        result.Value.Items.Select(x => x.Id).ShouldBe(_store.Rows.Take(20).Select(x => x.Id));
        result.Value.ToString().ShouldBe("SavedTravelerPage");
        result.Value.Items[0].ToString().ShouldBe("SavedTravelerView");
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("revision")]
    [InlineData("oversized")]
    [InlineData("envelope")]
    public async Task Malformed_list_fails_closed_before_any_decryption(string kind)
    {
        var row = Record();
        _store.Rows = kind switch
        {
            "foreign" => [row, Record(new(Guid.NewGuid())) with { OwnerUserId = Guid.NewGuid() }],
            "duplicate" => [row, row],
            "revision" => [row with { Revision = Guid.Empty }],
            "oversized" => Enumerable
                .Range(0, 22)
                .Select(_ => Record(new(Guid.NewGuid())))
                .ToArray(),
            _ => [row with { ProtectedDetails = null! }],
        };
        (
            await _service.ListAsync(Owner, 0, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        _calls.ShouldBe(["list"]);
    }

    [Fact]
    public async Task Unreadable_row_returns_no_partial_page()
    {
        _store.Rows = [Record(), Record(new(Guid.NewGuid()))];
        _protector.FailAt = 2;
        (
            await _service.ListAsync(Owner, 0, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        _calls.ShouldBe(["list", "unprotect", "unprotect"]);
    }

    [Fact]
    public async Task Future_decrypted_DOB_fails_closed()
    {
        _store.Record = Record();
        _protector.Details = SavedTravelerDetails
            .CreateRaw(
                "mr",
                "Fiction",
                "Traveler",
                new(2027, 1, 1),
                "male",
                "fiction@example.test",
                "+12025550123",
                new(2027, 1, 1)
            )
            .Value;
        (
            await _service.GetAsync(Owner, Id, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2147483647)]
    public async Task Invalid_page_never_touches_store_or_crypto(int offset)
    {
        (
            await _service.ListAsync(Owner, offset, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.TravelerPageInvalid");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_internal_identifiers_stop_before_storage_or_crypto()
    {
        (
            await _service.GetAsync(Guid.Empty, Id, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.TravelerIdentityInvalid");
        (
            await _service.CreateAsync(
                Owner,
                new(Guid.Empty),
                Details,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.TravelerIdInvalid");
        (
            await _service.UpdateAsync(
                Owner,
                Id,
                Guid.Empty,
                Details,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.TravelerPreconditionInvalid");
        (
            await _service.DeleteAsync(Owner, Id, Guid.Empty, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.TravelerPreconditionInvalid");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Protection_failure_never_stores()
    {
        _protector.Unavailable = true;
        (
            await _service.CreateAsync(Owner, Id, Details, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PiiProtectionUnavailable");
        _calls.ShouldBe(["visibility", "protect"]);
    }

    [Fact]
    public async Task Storage_failure_is_preserved_without_crypto()
    {
        _store.Unavailable = true;
        (
            await _service.ListAsync(Owner, 0, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.TravelerStorageUnavailable");
        _calls.ShouldBe(["list"]);
    }

    [Fact]
    public async Task Malformed_owned_identity_fails_before_decryption_or_protection()
    {
        _store.Record = Record(new(Guid.NewGuid()));
        (
            await _service.GetAsync(Owner, Id, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        (
            await _service.UpdateAsync(
                Owner,
                Id,
                _store.Record.Revision,
                Details,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        _calls.ShouldBe(["get", "get"]);
    }

    [Fact]
    public async Task Maximum_valid_offset_is_allowed_without_overflow()
    {
        var result = await _service.ListAsync(
            Owner,
            SavedTravelerPaging.MaximumOffset,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Items.ShouldBeEmpty();
        result.Value.HasMore.ShouldBeFalse();
        _store.Offset.ShouldBe(2147483640);
        _calls.ShouldBe(["list"]);
    }

    [Fact]
    public async Task Future_details_cannot_be_created_or_updated()
    {
        var future = SavedTravelerDetails
            .CreateRaw(
                "mr",
                "Fiction",
                "Traveler",
                new(2027, 1, 1),
                "male",
                "fiction@example.test",
                "+12025550123",
                new(2027, 1, 1)
            )
            .Value;
        (
            await _service.CreateAsync(Owner, Id, future, TestContext.Current.CancellationToken)
        ).FirstError.Code.ShouldBe("Flights.PassengerDateOfBirthFutureInvalid");
        _store.Record = Record();
        (
            await _service.UpdateAsync(
                Owner,
                Id,
                _store.Record.Revision,
                future,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.PassengerDateOfBirthFutureInvalid");
        _calls.ShouldBe(["visibility", "get"]);
    }

    [Fact]
    public async Task Delete_maps_atomic_owned_stale_and_missing_without_crypto()
    {
        _store.WriteOutcome = SavedTravelerWriteOutcome.PreconditionFailed;
        (
            await _service.DeleteAsync(
                Owner,
                Id,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.TravelerPreconditionFailed");
        _store.WriteOutcome = SavedTravelerWriteOutcome.NotFound;
        (
            await _service.DeleteAsync(
                Owner,
                Id,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Flights.TravelerNotFound");
        _calls.ShouldBe(["delete", "delete"]);
    }

    private static SavedTravelerStoredRecord Record(SavedTravelerId? id = null) =>
        new(id ?? Id, Owner, Guid.NewGuid(), Envelope, Now, Now);

    private sealed class FakeProtector(List<string> calls) : ISavedTravelerProtector
    {
        public SavedTravelerProtectionContext? Context;
        public bool Unavailable;
        public int FailAt;
        private int _reads;
        public SavedTravelerDetails Details = SavedTravelerServiceTests.Details;

        public ErrorOr<ProtectedSavedTravelerSnapshot> Protect(
            SavedTravelerProtectionContext context,
            SavedTravelerDetails details
        )
        {
            calls.Add("protect");
            Context = context;
            return Unavailable ? PiiProtectionErrors.Unavailable : Envelope;
        }

        public ErrorOr<SavedTravelerDetails> Unprotect(
            SavedTravelerProtectionContext context,
            ProtectedSavedTravelerSnapshot snapshot
        )
        {
            calls.Add("unprotect");
            Context = context;
            return Unavailable || ++_reads == FailAt
                ? PiiProtectionErrors.PayloadUnavailable
                : Details;
        }
    }

    private sealed class FakeStore(List<string> calls) : ISavedTravelerStore
    {
        public SavedTravelerStoredRecord? Record;
        public IReadOnlyList<SavedTravelerStoredRecord> Rows = [];
        public SavedTravelerStoredRecord? Written;
        public SavedTravelerVisibility Visibility = SavedTravelerVisibility.Missing;
        public SavedTravelerWriteOutcome WriteOutcome = SavedTravelerWriteOutcome.Succeeded;
        public Guid Expected;
        public int Offset;
        public bool Unavailable;

        public Task<ErrorOr<SavedTravelerStoredRecord?>> GetOwnedAsync(
            Guid ownerUserId,
            SavedTravelerId id,
            CancellationToken ct
        )
        {
            calls.Add("get");
            return Task.FromResult<ErrorOr<SavedTravelerStoredRecord?>>(
                Record?.OwnerUserId == ownerUserId ? Record : (SavedTravelerStoredRecord?)null
            );
        }

        public Task<ErrorOr<IReadOnlyList<SavedTravelerStoredRecord>>> ListOwnedAsync(
            Guid ownerUserId,
            int offset,
            CancellationToken ct
        )
        {
            calls.Add("list");
            Offset = offset;
            return Task.FromResult(
                Unavailable
                    ? ErrorOr<IReadOnlyList<SavedTravelerStoredRecord>>.From([
                        SavedTravelerErrors.StorageUnavailable,
                    ])
                    : ErrorOrFactory.From<IReadOnlyList<SavedTravelerStoredRecord>>(Rows)
            );
        }

        public Task<ErrorOr<SavedTravelerVisibility>> GetVisibilityAsync(
            Guid ownerUserId,
            SavedTravelerId id,
            CancellationToken ct
        )
        {
            calls.Add("visibility");
            return Task.FromResult<ErrorOr<SavedTravelerVisibility>>(Visibility);
        }

        public Task<ErrorOr<SavedTravelerWriteOutcome>> CreateAsync(
            SavedTravelerStoredRecord record,
            CancellationToken ct
        )
        {
            calls.Add("create");
            Written = record;
            return Task.FromResult<ErrorOr<SavedTravelerWriteOutcome>>(WriteOutcome);
        }

        public Task<ErrorOr<SavedTravelerWriteOutcome>> UpdateAsync(
            SavedTravelerStoredRecord replacement,
            Guid expectedRevision,
            CancellationToken ct
        )
        {
            calls.Add("update");
            Written = replacement;
            Expected = expectedRevision;
            return Task.FromResult<ErrorOr<SavedTravelerWriteOutcome>>(WriteOutcome);
        }

        public Task<ErrorOr<SavedTravelerWriteOutcome>> DeleteAsync(
            Guid ownerUserId,
            SavedTravelerId id,
            Guid expectedRevision,
            CancellationToken ct
        )
        {
            calls.Add("delete");
            Expected = expectedRevision;
            return Task.FromResult<ErrorOr<SavedTravelerWriteOutcome>>(WriteOutcome);
        }
    }
}
