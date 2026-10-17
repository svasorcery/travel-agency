using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelPurchaseTests
{
    [Fact]
    public async Task Quote_expiring_inside_private_protection_is_refused_before_claim_and_releases_capacity()
    {
        var fixture = new Fixture("alternative", false);
        var clock = new AdmissionClock(DateTimeOffset.UtcNow);
        var protector = new ExpiringProtector(clock);
        var service = new HotelReservationService(
            fixture.Store,
            [fixture.Provider],
            protector,
            fixture.Capacity,
            clock
        );
        var quote = (
            await service.QuoteAsync(
                fixture.Owner,
                Guid.NewGuid(),
                null,
                fixture.Selection,
                TestContext.Current.CancellationToken
            )
        ).Value;
        protector.ExpireAt = quote.ValidUntil;
        var result = await service.PurchaseAsync(
            fixture.Owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, fixture.Party),
            TestContext.Current.CancellationToken
        );
        result.FirstError.ShouldBe(HotelErrors.Expired);
        fixture.Store.Row!.AttemptId.ShouldBeNull();
        fixture.Provider.Creates.ShouldBe(0);
        fixture.Capacity.Active.ShouldBe(0);
    }

    private sealed class AdmissionClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpiringProtector(AdmissionClock clock) : IHotelPrivateProtector
    {
        private readonly TestProtector inner = new();
        public DateTimeOffset? ExpireAt { get; set; }

        public ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding)
        {
            if (binding.Kind == "party" && ExpireAt is { } expiry)
                clock.Now = expiry;
            return inner.Protect(plaintext, binding);
        }

        public ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding) =>
            inner.Unprotect(envelope, binding);
    }

    [Theory]
    [InlineData("baseline", true)]
    [InlineData("alternative", false)]
    public async Task Same_flow_accepts_adapter_with_or_without_preparation_resource(
        string key,
        bool preparationResource
    )
    {
        var fixture = new Fixture(key, preparationResource);
        var quote = await fixture.Service.QuoteAsync(
            fixture.Owner,
            Guid.NewGuid(),
            null,
            fixture.Selection,
            TestContext.Current.CancellationToken
        );
        quote.IsError.ShouldBeFalse();
        var purchase = new HotelPurchase(
            Guid.NewGuid(),
            quote.Value.QuoteRevision,
            true,
            fixture.Party
        );
        var result = await fixture.Service.PurchaseAsync(
            fixture.Owner,
            quote.Value.Id,
            purchase,
            TestContext.Current.CancellationToken
        );
        result.Value.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        fixture.Provider.Creates.ShouldBe(1);
        fixture.Capacity.Available = false;
        var replay = await fixture.Service.PurchaseAsync(
            fixture.Owner,
            quote.Value.Id,
            purchase,
            TestContext.Current.CancellationToken
        );
        replay.Value.AttemptId.ShouldBe(result.Value.AttemptId);
        var changed = await fixture.Service.PurchaseAsync(
            fixture.Owner,
            quote.Value.Id,
            purchase with
            {
                Party = fixture.Party with { Email = "different@example.invalid" },
            },
            TestContext.Current.CancellationToken
        );
        changed.FirstError.ShouldBe(HotelErrors.Conflict);
        fixture.Provider.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task No_capacity_leaves_quote_and_claim_unspent()
    {
        var fixture = new Fixture("alternative", false);
        var quote = (
            await fixture.Service.QuoteAsync(
                fixture.Owner,
                Guid.NewGuid(),
                null,
                fixture.Selection,
                TestContext.Current.CancellationToken
            )
        ).Value;
        fixture.Capacity.Available = false;
        (
            await fixture.Service.PurchaseAsync(
                fixture.Owner,
                quote.Id,
                new(Guid.NewGuid(), quote.QuoteRevision, true, fixture.Party),
                TestContext.Current.CancellationToken
            )
        ).FirstError.ShouldBe(HotelErrors.Capacity);
        fixture.Store.Row!.AttemptId.ShouldBeNull();
        fixture.Provider.Creates.ShouldBe(0);
    }

    internal sealed class Fixture
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public FakeStore Store { get; } = new();
        public FakeCapacity Capacity { get; } = new();
        public SemanticBookingProvider Provider { get; }
        public HotelReservationService Service { get; }
        public SelectedStay Selection { get; }
        public HotelParty Party { get; } =
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            );

        public Fixture(string key, bool reference)
        {
            Provider = new(key, reference);
            Service = new(Store, [Provider], new TestProtector(), Capacity, TimeProvider.System);
            var terms = ReservationStateTests.Terms();
            Selection = new(
                new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
                new(key, "Test", "rate_1", terms, DateTimeOffset.UtcNow.AddMinutes(5), true, null)
            );
        }
    }

    internal sealed class SemanticBookingProvider(string key, bool reference)
        : IHotelBookingProvider
    {
        public string Key => key;
        public int Creates { get; private set; }

        public CapabilitySupport GetSupport(SelectedStay selection) =>
            (
                selection.Request.Occupancy.Adults == 1
                || reference && selection.Request.Occupancy.Adults == 2
            )
                ? CapabilitySupport.Yes
                : CapabilitySupport.No("AdultsUnsupported");

        public Task<Preparation> PrepareAsync(
            SelectedStay selection,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                GetSupport(selection).Supported
                    ? new Preparation(
                        PreparationKind.Ready,
                        new(
                            selection.Offer.Terms,
                            DateTimeOffset.UtcNow.AddMinutes(5),
                            new(1, "opaque-context"),
                            null,
                            reference ? "prep_fictional" : null
                        )
                    )
                    : new Preparation(PreparationKind.Unsupported, Reason: "AdultsUnsupported")
            );

        public Task<BookingObservation> CreateAsync(
            CreateStay purchase,
            CancellationToken cancellationToken
        )
        {
            Creates++;
            return Task.FromResult(
                new BookingObservation(
                    ObservationKind.Confirmed,
                    new("book_fictional", "hotel_fictional", purchase.Accepted)
                )
            );
        }
    }

    internal sealed class FakeCapacity : IHotelCapacity
    {
        public bool Available { get; set; } = true;
        public int Active { get; private set; }

        public IDisposable? TryAcquire(string provider, bool recovery = false)
        {
            if (!Available)
                return null;
            Active++;
            return new Lease(() => Active--);
        }

        private sealed class Lease(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }

    internal sealed class TestProtector : IHotelPrivateProtector
    {
        public ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding) =>
            JsonSerializer.Serialize(new { binding, plaintext });

        public ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding)
        {
            using var json = JsonDocument.Parse(envelope);
            return
                JsonSerializer.Serialize(binding)
                == json.RootElement.GetProperty("binding").GetRawText()
                ? json.RootElement.GetProperty("plaintext").GetString()!
                : HotelErrors.Unavailable;
        }
    }

    internal sealed class FakeStore : IHotelReservationStore
    {
        public HotelReservation? Row { get; private set; }

        public Task<ErrorOr<HotelReservation?>> LoadAsync(
            Guid id,
            Guid? owner,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<HotelReservation?>>(
                Row is not null && Row.Id == id && (owner is null || Row.OwnerId == owner)
                    ? Clone(Row)
                    : null
            );

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            Task.FromResult(
                ErrorOrFactory.From<HotelReservation[]>(Row?.OwnerId == owner ? [Clone(Row)] : [])
            );

        public Task<ErrorOr<bool>> SaveAsync(
            HotelReservation reservation,
            int expectedVersion,
            HotelWork[] work,
            CancellationToken ct
        )
        {
            if ((Row?.Version ?? 0) != expectedVersion)
                return Task.FromResult<ErrorOr<bool>>(false);
            reservation.Version = expectedVersion + 1;
            Row = Clone(reservation);
            return Task.FromResult<ErrorOr<bool>>(true);
        }

        private static HotelReservation Clone(HotelReservation row) =>
            JsonSerializer.Deserialize<HotelReservation>(JsonSerializer.Serialize(row))!;
    }
}
