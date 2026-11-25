using System.Text;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsCodecProtectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static OwnerIdentity Owner =>
        OwnerIdentity
            .Create(
                "https://fictional.trips.test",
                Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
            )
            .Value;
    internal static TripBriefInput Brief =>
        new(
            "saint-petersburg",
            "moscow",
            new(2026, 11, 14),
            new(2026, 11, 14),
            2,
            "RUB",
            "15000.00",
            ["architecture", "food"],
            "relaxed"
        );

    [Fact]
    public async Task Real_protection_roundtrip_all_binding_dimensions_and_tamper()
    {
        using var ring = new TripsTestRing();
        ring.Provider.IsReady.ShouldBeTrue();
        var binding = new TripPlanBinding(Owner, Guid.NewGuid(), 1);
        var encrypted = await ring.Protector.ProtectAsync("private fictional 🧭", binding, Ct);
        encrypted.IsError.ShouldBeFalse();
        encrypted.Value.ShouldNotContain("private fictional");
        (await ring.Protector.UnprotectAsync(encrypted.Value, binding, Ct)).Value.ShouldBe(
            new UnprotectedTripPlan("private fictional 🧭", 1)
        );
        (
            await ring.Protector.UnprotectAsync("v2:" + encrypted.Value, binding, Ct)
        ).FirstError.ShouldBe(TripErrors.Unavailable);
        var otherOwner = OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value;
        foreach (
            var mismatch in new[]
            {
                binding with
                {
                    Owner = otherOwner,
                },
                binding with
                {
                    Owner = OwnerIdentity
                        .Create("https://other.fictional.test", Owner.Subject)
                        .Value,
                },
                binding with
                {
                    TripId = Guid.NewGuid(),
                },
                binding with
                {
                    Revision = 2,
                },
            }
        )
            (
                await ring.Protector.UnprotectAsync(encrypted.Value, mismatch, Ct)
            ).IsError.ShouldBeTrue();
        var bytes = Convert.FromBase64String(encrypted.Value);
        bytes[^1] ^= 1;
        (
            await ring.Protector.UnprotectAsync(Convert.ToBase64String(bytes), binding, Ct)
        ).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_stays_use_a_version_marker_and_the_preceding_purpose_cannot_read_them()
    {
        using var ring = new TripsTestRing();
        var binding = new TripPlanBinding(Owner, Guid.NewGuid(), 2);
        const string plaintext =
            """{"manualStays":[{"placeName":"Fictional one"},{"placeName":"Fictional two"}]}""";
        var v2 = await ring.Protector.ProtectAsync(plaintext, binding, Ct, schemaVersion: 2);
        v2.IsError.ShouldBeFalse();
        v2.Value.ShouldStartWith("v2:");
        v2.Value.ShouldNotContain("Fictional");
        Should.Throw<FormatException>(() => Convert.FromBase64String(v2.Value));
        (await ring.Protector.UnprotectAsync(v2.Value, binding, Ct)).Value.ShouldBe(
            new UnprotectedTripPlan(plaintext, 2)
        );
        (await ring.Protector.UnprotectAsync(v2.Value[3..], binding, Ct)).FirstError.ShouldBe(
            TripErrors.Unavailable
        );
        (
            await ring.Protector.UnprotectAsync("v3:" + v2.Value[3..], binding, Ct)
        ).FirstError.ShouldBe(TripErrors.Unavailable);
    }

    [Fact]
    public async Task Manual_stay_is_canonical_and_remains_in_the_protected_size_budget()
    {
        using var ring = new TripsTestRing();
        var now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        var brief = Brief with { EndDate = Brief.StartDate.AddDays(1) };
        var empty = TripPlan.Create(Guid.NewGuid(), Owner, "Fictional", brief, now).Value;
        var days = empty.Days.Select(day => new TripDayInput(day.Date, [])).ToArray();
        var stay = new TripStayInput("Вымышленное место 🧭", brief.StartDate, brief.EndDate);
        var plan = empty.Replace(empty.Title, brief, days, now, [stay]).Value;
        var codec = new TripPlanJsonCodec();
        var text = codec.Serialize(plan).Value;
        var row = new TripPlanRecord(plan.TripId, Owner, plan.Revision, now, now, "opaque");
        var binding = new TripPlanBinding(Owner, plan.TripId, plan.Revision);
        var protectedBody = (await ring.Protector.ProtectAsync(text, binding, Ct)).Value;

        protectedBody.ShouldNotContain(stay.PlaceName);
        var plaintext = (await ring.Protector.UnprotectAsync(protectedBody, binding, Ct)).Value;
        codec
            .Deserialize(plaintext.Plaintext, row, plaintext.SchemaVersion)
            .Value.ManualStays.Single()
            .ToInput()
            .ShouldBe(stay);
        codec
            .MeasureDetailBytes(plan, true)
            .Value.ShouldBeGreaterThan(codec.MeasureDetailBytes(empty, true).Value);
        codec
            .Deserialize(
                text.Replace("\"checkOutDate\"", "\"unexpected\"", StringComparison.Ordinal),
                row
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Cursor_is_bounded_owner_scoped_and_tamper_safe()
    {
        using var ring = new TripsTestRing();
        var cursor = new TripPlanCursor(ring.Provider);
        var position = new TripPlanPosition(DateTimeOffset.UtcNow, Guid.NewGuid());
        var encoded = cursor.Encode(Owner, position);
        encoded.IsError.ShouldBeFalse();
        encoded.Value.Length.ShouldBeLessThanOrEqualTo(256);
        encoded.Value.ShouldNotContain(Owner.Subject.ToString());
        cursor.Decode(Owner, encoded.Value).Value.ShouldBe(position);
        cursor
            .Decode(OwnerIdentity.Create(Owner.Issuer, Guid.NewGuid()).Value, encoded.Value)
            .IsError.ShouldBeTrue();
        cursor.Decode(Owner, encoded.Value + "?").IsError.ShouldBeTrue();
    }

    [Fact]
    public void Canonical_full_detail_and_server_reserve_use_same_bytes_and_validate_binding()
    {
        var now = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        var plan = TripPlan.Create(Guid.NewGuid(), Owner, "Исторический 🧭", Brief, now).Value;
        var codec = new TripPlanJsonCodec();
        var text = codec.Serialize(plan).Value;
        var row = new TripPlanRecord(plan.TripId, Owner, 1, now, now, "opaque");
        codec.MeasureDetailBytes(plan, false).Value.ShouldBe(Encoding.UTF8.GetByteCount(text));
        codec
            .MeasureDetailBytes(plan, true)
            .Value.ShouldBe(codec.MeasureDetailBytes(plan, false).Value + 18);
        codec.Deserialize(text, row).IsError.ShouldBeFalse();
        codec.Deserialize(text, row with { Revision = 2 }).IsError.ShouldBeTrue();
        codec.Deserialize(text, row with { TripId = Guid.NewGuid() }).IsError.ShouldBeTrue();
        codec.Deserialize(text, row with { UpdatedAt = now.AddTicks(10) }).IsError.ShouldBeTrue();
        text.ShouldContain("\"originId\":\"moscow\"");
        text.ShouldContain("\"revision\":\"1\"");
        text.ShouldNotContain("owner");
    }

    [Fact]
    public async Task Corruption_in_twentieth_row_fails_whole_list_without_partial_results()
    {
        using var ring = new TripsTestRing();
        var store = new MemoryTripStore();
        var codec = new TripPlanJsonCodec();
        var service = new TripPlanService(
            store,
            ring.Protector,
            codec,
            new TripPlanCursor(ring.Provider),
            TimeProvider.System
        );
        for (var index = 0; index < 20; index++)
            (
                await service.CreateAsync(Owner, new("private summary " + index, Brief), Ct)
            ).IsError.ShouldBeFalse();
        var last = store
            .Rows.Values.OrderByDescending(row => row.UpdatedAt)
            .ThenBy(row => row.TripId)
            .Last();
        store.Rows[last.TripId] = last with { ProtectedPayload = "tampered" };
        var listed = await service.ListAsync(Owner, null, Ct);
        listed.IsError.ShouldBeTrue();
        listed.FirstError.Code.ShouldBe("Trips.Unavailable");
    }

    [Fact]
    public void Twenty_maximum_unicode_summaries_stay_bounded_and_cost_fields_absent()
    {
        var page = new TripPlanPage(
            Enumerable
                .Range(0, 20)
                .Select(_ => new TripPlanSummary(
                    Guid.NewGuid(),
                    long.MaxValue,
                    string.Concat(Enumerable.Repeat("🧭", 100)),
                    "saint-petersburg",
                    "Санкт-Петербург",
                    new(2026, 11, 14),
                    new(2026, 11, 27),
                    DateTimeOffset.MaxValue
                ))
                .ToArray(),
            new string('a', 256)
        );
        var codec = new TripPlanJsonCodec();
        var text = codec.SerializePage(page).Value;
        codec.MeasurePageBytes(page).Value.ShouldBe(Encoding.UTF8.GetByteCount(text));
        codec.MeasurePageBytes(page).Value.ShouldBeLessThanOrEqualTo(128 * 1024);
        text.ShouldNotContain("blocks");
        text.ShouldNotContain("note");
        text.ShouldNotContain("interests");
        text.ShouldNotContain("Budget");
    }
}
