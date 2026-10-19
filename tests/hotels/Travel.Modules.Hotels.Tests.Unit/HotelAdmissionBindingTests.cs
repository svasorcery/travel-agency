using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelAdmissionBindingTests
{
    [Fact]
    public async Task Source_party_limit_refuses_before_claim_without_reducing_other_sources_names()
    {
        var store = new HotelPurchaseTests.FakeStore();
        var capacity = new HotelPurchaseTests.FakeCapacity();
        var provider = new BoundProvider();
        var service = new HotelReservationService(
            store,
            [provider],
            new HotelPurchaseTests.TestProtector(),
            capacity,
            TimeProvider.System
        );
        var terms = ReservationStateTests.Terms();
        var owner = Guid.NewGuid();
        var quote = (
            await service.QuoteAsync(
                owner,
                Guid.NewGuid(),
                null,
                new(
                    new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
                    new("hotelbeds", "Test", "local", terms, null, true, null)
                ),
                TestContext.Current.CancellationToken
            )
        ).Value;
        var party = new HotelParty(
            [new(new string('a', 51), "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        party.IsValid(1).ShouldBeTrue();
        var result = await service.PurchaseAsync(
            owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, party),
            TestContext.Current.CancellationToken
        );
        result.FirstError.ShouldBe(HotelErrors.Unsupported);
        store.Row!.AttemptId.ShouldBeNull();
        provider.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Recognized_legacy_context_remains_admissible_when_provider_gains_descriptor()
    {
        var fixture = new HotelPurchaseTests.Fixture("duffel-stays", true);
        var quote = (
            await fixture.Service.QuoteAsync(
                fixture.Owner,
                Guid.NewGuid(),
                null,
                fixture.Selection,
                TestContext.Current.CancellationToken
            )
        ).Value;
        var upgraded = new LegacyDescriptorProvider(fixture.Provider);
        var service = new HotelReservationService(
            fixture.Store,
            [upgraded],
            new HotelPurchaseTests.TestProtector(),
            fixture.Capacity,
            TimeProvider.System
        );
        var result = await service.PurchaseAsync(
            fixture.Owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, fixture.Party),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        fixture.Provider.Creates.ShouldBe(1);
    }

    [Theory]
    [InlineData("approval")]
    [InlineData("account")]
    [InlineData("profile")]
    [InlineData("missing")]
    [InlineData("mapping")]
    public async Task Changed_current_authority_refuses_before_capacity_and_claim(string change)
    {
        var store = new HotelPurchaseTests.FakeStore();
        var capacity = new HotelPurchaseTests.FakeCapacity();
        var provider = new BoundProvider();
        var service = new HotelReservationService(
            store,
            [provider],
            new HotelPurchaseTests.TestProtector(),
            capacity,
            TimeProvider.System
        );
        var terms = ReservationStateTests.Terms();
        var selection = new SelectedStay(
            new(HotelSearchService.Areas[3], terms.Dates, terms.Occupancy),
            new("hotelbeds", "Test", "local", terms, null, true, null)
        );
        var owner = Guid.NewGuid();
        var quote = (
            await service.QuoteAsync(
                owner,
                Guid.NewGuid(),
                null,
                selection,
                TestContext.Current.CancellationToken
            )
        ).Value;
        switch (change)
        {
            case "approval":
                provider.CurrentSource = provider.CurrentSource with { NewPurchaseAllowed = false };
                break;
            case "account":
                provider.CurrentSource = provider.CurrentSource with
                {
                    Identity = provider.CurrentSource.Identity with { Account = "other" },
                };
                break;
            case "profile":
                provider.CurrentSource = provider.CurrentSource with
                {
                    Identity = provider.CurrentSource.Identity with { Profile = "External" },
                };
                break;
            case "missing":
                var row = (
                    await store.LoadAsync(quote.Id, owner, TestContext.Current.CancellationToken)
                ).Value!;
                row.State.AdmissionBinding = null;
                (
                    await store.SaveAsync(
                        row,
                        row.Version,
                        [],
                        TestContext.Current.CancellationToken
                    )
                ).IsError.ShouldBeFalse();
                break;
            case "mapping":
                var mapped = (
                    await store.LoadAsync(quote.Id, owner, TestContext.Current.CancellationToken)
                ).Value!;
                mapped.State.AdmissionBinding = mapped.State.AdmissionBinding! with
                {
                    MappingPair = "removed-pair",
                    MappingRevision = "r1",
                };
                (
                    await store.SaveAsync(
                        mapped,
                        mapped.Version,
                        [],
                        TestContext.Current.CancellationToken
                    )
                ).IsError.ShouldBeFalse();
                break;
        }
        var party = new HotelParty(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );
        var result = await service.PurchaseAsync(
            owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, party),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        store.Row!.AttemptId.ShouldBeNull();
        provider.Creates.ShouldBe(0);
        capacity.Active.ShouldBe(0);
    }

    private sealed class BoundProvider
        : IHotelBookingProvider,
            IHotelSourceDescriptor,
            IHotelPartySupport
    {
        public string Key => "hotelbeds";
        public HotelSourceDescriptor CurrentSource { get; set; } =
            new(
                new("hotelbeds", "fictional-account", "Test", "Fictional"),
                "config-1",
                "Net",
                "owned-1",
                true
            );
        public int Creates { get; private set; }

        public CapabilitySupport GetPartySupport(HotelParty party) =>
            party.Guests.Any(g => g.GivenName.Length > 50 || g.FamilyName.Length > 50)
                ? CapabilitySupport.No("GuestNameUnsupported")
                : CapabilitySupport.Yes;

        public CapabilitySupport GetSupport(SelectedStay selection) => CapabilitySupport.Yes;

        public Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken ct) =>
            Task.FromResult(
                new Preparation(
                    PreparationKind.Ready,
                    new(
                        selection.Offer.Terms,
                        DateTimeOffset.UtcNow.AddMinutes(5),
                        new(2, "opaque"),
                        AccountBinding: CurrentSource.Identity.Account,
                        AdmissionBinding: new(
                            2,
                            CurrentSource.Identity,
                            "config-1",
                            "Net",
                            "owned-1",
                            null,
                            null,
                            DateTimeOffset.UtcNow.AddMinutes(5)
                        )
                    )
                )
            );

        public Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken ct)
        {
            Creates++;
            return Task.FromResult(new BookingObservation(ObservationKind.Unknown));
        }
    }

    private sealed class LegacyDescriptorProvider(HotelPurchaseTests.SemanticBookingProvider inner)
        : IHotelBookingProvider,
            IHotelSourceDescriptor,
            IHotelLegacyContextConsumer
    {
        public string Key => inner.Key;
        public HotelSourceDescriptor CurrentSource =>
            new(new(Key, "new", "Test", "External"), "new", "Net", "new", false);

        public bool AcceptsLegacyContext(ProviderContextHandle context) =>
            context.Version == 1 && context.Value == "opaque-context";

        public CapabilitySupport GetSupport(SelectedStay selection) => inner.GetSupport(selection);

        public Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken ct) =>
            inner.PrepareAsync(selection, ct);

        public Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken ct) =>
            inner.CreateAsync(purchase, ct);
    }
}
