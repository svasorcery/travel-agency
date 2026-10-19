using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelDeadlineTests
{
    [Theory]
    [InlineData("hotelbeds", 90)]
    [InlineData("duffel-stays", 140)]
    public async Task Source_HTTP_deadline_is_source_specific_and_never_proves_no_creation(
        string key,
        int seconds
    )
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var wire = new CancellationWire();
        var capacityOptions = Options.Create(new HotelsOptions());
        var capacity = new HotelCapacity(capacityOptions, time);
        IHotelBookingProvider provider;
        ProviderContextHandle context;
        if (key == "hotelbeds")
        {
            var options = Options.Create(
                new HotelbedsOptions
                {
                    Enabled = true,
                    BookingEnabled = true,
                    Fictional = true,
                    AccountAlias = "owned",
                    ConfigurationRevision = "owned",
                    ApprovalRevision = "owned",
                    ApiKey = "owned-fictional-key",
                    Secret = "owned-fictional-secret",
                }
            );
            var hb = new HotelbedsStayProvider(
                new HotelbedsFictionalHttpClient(wire),
                options,
                capacity,
                time
            );
            provider = hb;
            context = new(
                2,
                JsonSerializer.Serialize(
                    new
                    {
                        source = hb.CurrentSource.Identity,
                        rateKey = "owned-opaque",
                        credentialIdentity = hb.CurrentSource.TransportCredentialFingerprint,
                    },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                )
            );
        }
        else
        {
            var options = Options.Create(
                new HotelsOptions
                {
                    Enabled = true,
                    BookingEnabled = true,
                    ReceiverEnabled = true,
                    ApiKey = "duffel_test_owned_only",
                    WebhookSecret = "owned-fictional-secret",
                }
            );
            provider = new DuffelStaysProvider(new HttpClient(wire), options, capacity, time);
            context = new(1, "{\"QuoteId\":\"quo_owned\",\"RateId\":\"rat_owned\"}");
        }
        var pending = provider.CreateAsync(
            new(Guid.NewGuid(), Guid.NewGuid(), DuffelStaysProviderTests.Terms(), context, Party()),
            TestContext.Current.CancellationToken
        );
        await wire.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromSeconds(seconds - 1));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(ObservationKind.Unknown);
        result.Facts.ShouldBeNull();
        wire.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Application_sender_has_145_second_cancellation_separate_from_HTTP()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new OwnedHotelMemoryStore();
        var source = new ClockBookingProvider(time, true);
        var capacity = new HotelCapacity(Options.Create(new HotelsOptions()), time);
        var service = new HotelReservationService(
            store,
            [source],
            new OwnedHotelMemoryProtector(),
            capacity,
            time
        );
        var owner = Guid.NewGuid();
        var quote = (
            await service.QuoteAsync(
                owner,
                Guid.NewGuid(),
                null,
                Selection(),
                TestContext.Current.CancellationToken
            )
        ).Value;
        var pending = service.PurchaseAsync(
            owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, Party()),
            TestContext.Current.CancellationToken
        );
        await source.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromSeconds(144));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        (
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)
        ).Value.Outcome.ShouldBe(ReservationOutcome.Unknown);
        store.Snapshot(quote.Id)!.AttemptId.ShouldNotBeNull();
        source.Creates.ShouldBe(1);
        capacity.Active.ShouldBe(0);
    }

    [Fact]
    public async Task Completion_store_has_its_own_10_second_budget_and_keeps_the_spent_claim()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var inner = new OwnedHotelMemoryStore();
        var store = new DelayedCompletionStore(inner, time);
        var source = new ClockBookingProvider(time, false);
        var service = new HotelReservationService(
            store,
            [source],
            new OwnedHotelMemoryProtector(),
            new HotelCapacity(Options.Create(new HotelsOptions()), time),
            time
        );
        var owner = Guid.NewGuid();
        var quote = (
            await service.QuoteAsync(
                owner,
                Guid.NewGuid(),
                null,
                Selection(),
                TestContext.Current.CancellationToken
            )
        ).Value;
        var pending = service.PurchaseAsync(
            owner,
            quote.Id,
            new(Guid.NewGuid(), quote.QuoteRevision, true, Party()),
            TestContext.Current.CancellationToken
        );
        await store.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromSeconds(9));
        pending.IsCompleted.ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        inner.Snapshot(quote.Id)!.AttemptId.ShouldNotBeNull();
        source.Creates.ShouldBe(1);
    }

    private static HotelParty Party() =>
        new(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );

    private static SelectedStay Selection()
    {
        var terms = DuffelStaysProviderTests.Terms();
        return new(
            new(HotelSearchService.Areas[0], terms.Dates, terms.Occupancy),
            new("duffel-stays", "Test", "owned", terms, null, true, null)
        );
    }

    private sealed class CancellationWire : HttpMessageHandler, IHotelbedsFictionalTransport
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }

    private sealed class ClockBookingProvider(TimeProvider time, bool delay)
        : IHotelBookingProvider,
            IHotelLegacyContextConsumer
    {
        public string Key => "duffel-stays";
        public int Creates { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool AcceptsLegacyContext(ProviderContextHandle handle) =>
            handle is { Version: 1, Value: "owned-clock-context" };

        public CapabilitySupport GetSupport(SelectedStay selection) => CapabilitySupport.Yes;

        public Task<Preparation> PrepareAsync(SelectedStay selection, CancellationToken ct) =>
            Task.FromResult(
                new Preparation(
                    PreparationKind.Ready,
                    new(
                        selection.Offer.Terms,
                        time.GetUtcNow().AddMinutes(5),
                        new(1, "owned-clock-context")
                    )
                )
            );

        public async Task<BookingObservation> CreateAsync(CreateStay purchase, CancellationToken ct)
        {
            Creates++;
            Started.TrySetResult();
            if (delay)
                await Task.Delay(Timeout.InfiniteTimeSpan, time, ct);
            return new(ObservationKind.Confirmed, new("owned-booking", null, purchase.Accepted));
        }
    }

    private sealed class DelayedCompletionStore(OwnedHotelMemoryStore inner, TimeProvider time)
        : IHotelReservationStore
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ErrorOr<HotelReservation?>> LoadAsync(
            Guid id,
            Guid? owner,
            CancellationToken ct
        ) => inner.LoadAsync(id, owner, ct);

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            inner.ListAsync(owner, ct);

        public async Task<ErrorOr<bool>> SaveAsync(
            HotelReservation row,
            int expected,
            HotelWork[] work,
            CancellationToken ct
        )
        {
            if (row.SenderFinished)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, time, ct);
            }
            return await inner.SaveAsync(row, expected, work, ct);
        }
    }
}
