using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelLegacySerializedTests
{
    [Fact]
    public async Task Pre_M2_serialized_state_context_read_replay_and_actual_matching_keep_their_meaning()
    {
        var ct = TestContext.Current.CancellationToken;
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "m1-reservation.json"),
            ct
        );
        var context = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "m1-duffel-context.json"),
            ct
        );
        json.ShouldNotContain("AdmissionBinding");
        json.ShouldNotContain("QuoteContractVersion");
        json.ShouldNotContain("AcceptedConditions");
        JsonSerializer.Deserialize<ProviderContextHandle>(context)!.Version.ShouldBe(1);
        var row = JsonSerializer.Deserialize<HotelReservation>(json)!;
        row.State.QuoteContractVersion.ShouldBe(0);
        row.State.AdmissionBinding.ShouldBeNull();
        var termsBefore = JsonSerializer.Serialize(row.Terms);
        var protector = new CountingProtector();
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        row.ProtectedContext = protector
            .Protect(context, HotelReservationService.Binding(row, "context"))
            .Value;
        row.ProtectedParty = protector
            .Protect(JsonSerializer.Serialize(party), HotelReservationService.Binding(row, "party"))
            .Value;
        var store = new OwnedHotelMemoryStore();
        (await store.SaveAsync(row, 0, [], ct)).Value.ShouldBeTrue();
        var capacity = new NeverCapacity();
        var provider = new NeverProvider();
        var service = new HotelReservationService(
            store,
            [provider],
            protector,
            capacity,
            TimeProvider.System
        );
        protector.Offline = true;
        var detail = (await service.DetailAsync(row.OwnerId, row.Id, ct)).Value;
        detail.Outcome.ShouldBe(ReservationOutcome.Unknown);
        detail.Source.ShouldBeNull();
        detail.AcceptedConditions.ShouldBeNull();
        protector.Decrypts.ShouldBe(0);
        protector.Offline = false;
        var purchase = new HotelPurchase(row.ActionId!.Value, 1, true, party);
        (await service.PurchaseAsync(row.OwnerId, row.Id, purchase, ct)).Value.AttemptId.ShouldBe(
            row.AttemptId
        );
        (
            await service.PurchaseAsync(
                row.OwnerId,
                row.Id,
                purchase with
                {
                    ActionId = Guid.NewGuid(),
                },
                ct
            )
        ).FirstError.ShouldBe(HotelErrors.Conflict);
        capacity.Acquires.ShouldBe(0);
        provider.Creates.ShouldBe(0);
        var actual = row.Terms! with { RateId = "different-stage-reference" };
        ReservationState.TermsMatch(row.Terms!, actual).ShouldBeTrue();
        var updated = await service.ApplyAsync(
            row.Id,
            new(ObservationKind.Confirmed, new("bok_legacy_owned", null, actual)),
            true,
            ct
        );
        updated.Value.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        updated.Value.ReviewReasons.ShouldNotContain(ReviewReason.TermsMismatch);
        JsonSerializer.Serialize(store.Snapshot(row.Id)!.Terms).ShouldBe(termsBefore);
        store.Snapshot(row.Id)!.State.AdmissionBinding.ShouldBeNull();
    }

    private sealed class CountingProtector : IHotelPrivateProtector
    {
        private readonly OwnedHotelMemoryProtector inner = new();
        public bool Offline { get; set; }
        public int Decrypts { get; private set; }

        public ErrorOr<string> Protect(string value, HotelPrivacyBinding binding) =>
            inner.Protect(value, binding);

        public ErrorOr<string> Unprotect(string value, HotelPrivacyBinding binding)
        {
            Decrypts++;
            return Offline ? HotelErrors.Unavailable : inner.Unprotect(value, binding);
        }
    }

    private sealed class NeverCapacity : IHotelCapacity
    {
        public int Acquires { get; private set; }

        public IDisposable? TryAcquire(string provider, bool recovery = false)
        {
            Acquires++;
            throw new InvalidOperationException("Legacy replay must not acquire capacity.");
        }
    }

    private sealed class NeverProvider : IHotelBookingProvider
    {
        public string Key => "duffel-stays";
        public int Creates { get; private set; }

        public CapabilitySupport GetSupport(SelectedStay selection) =>
            CapabilitySupport.No("Disabled");

        public Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken ct) =>
            throw new InvalidOperationException("No prepare on replay.");

        public Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken ct)
        {
            Creates++;
            throw new InvalidOperationException("No create on replay.");
        }
    }
}
