using JasperFx;
using Marten;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

// This fixture is existing-CI only. Source compilation must not be mistaken for a database run.
public sealed class AncillaryMartenFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    public DocumentStore Store { get; private set; } = null!;
    public DifferenceCommitFailure Failure { get; } = new();
    public static readonly DateTimeOffset Now = new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        Store = DocumentStore.For(options =>
        {
            options.Connection(postgres.GetConnectionString());
            options.AutoCreateSchemaObjects = AutoCreate.All;
            FlightsModule.ConfigureMarten(options);
            options.Listeners.Add(Failure);
        });
    }

    public async ValueTask DisposeAsync()
    {
        Store.Dispose();
        await postgres.DisposeAsync();
    }

    public async Task<Guid> Quote()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var segment = Segment
            .Create(
                IataCode.Create("LHR").Value,
                IataCode.Create("CDG").Value,
                Now.AddDays(1),
                Now.AddDays(1).AddHours(1),
                "BA",
                "101",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
        var supplier = SupplierPassengerReference.Create("pas_fictional").Value;
        var party = BookableOfferParty
            .Create([new(supplier, BookingPassengerKind.Adult)], new(2030, 1, 2), true, false)
            .Value;
        var binding = QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new(
                        BookingPassengerId.Create(Guid.NewGuid()).Value,
                        supplier,
                        BookingPassengerKind.Adult
                    ),
                ]
            )
            .Value;
        var money = Money.Create(50, CurrencyCode.Create("GBP").Value).Value;
        var purchase = BookingPurchase
            .Create(
                binding.Revision,
                owner,
                money,
                Now.AddHours(1),
                [
                    new BookingService(
                        "ase_bag",
                        BookingServiceKind.CheckedBaggage,
                        binding.Slots[0].Id.Value,
                        new([new(0, 0)]),
                        2,
                        Money.Create(20, money.Currency).Value,
                        Money.Create(10, money.Currency).Value,
                        Baggage: new(23)
                    ),
                ]
            )
            .Value;
        await using var session = Store.LightweightSession();
        session.Events.StartStream<BookingAggregate>(
            id,
            new OfferQuoted(
                OfferId.New(),
                itinerary,
                money,
                purchase.ExpiresAt,
                "off_fictional",
                Now,
                new(false, false, null, null),
                binding
            ),
            new BookingPurchaseQuoted(purchase, Now)
        );
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    public static BookingCreationStarted Start(BookingAggregate booking) =>
        booking
            .DecideCreationStart(
                booking.OwnerUserId!.Value,
                Guid.NewGuid(),
                new string('a', 64),
                booking.QuoteBinding!.Revision,
                ProtectedPassengerPartySnapshot.Create(1, "opaque-cipher-fixture").Value,
                Guid.NewGuid(),
                Now,
                true
            )
            .Value.Event!;

    public sealed class DifferenceCommitFailure : DocumentSessionListenerBase
    {
        public bool Enabled { get; set; }

        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken ct)
        {
            if (
                Enabled
                && session
                    .PendingChanges.Streams()
                    .Any(s => s.Events.Any(e => e.Data is ConfirmationAttemptClosedWithoutEffects))
            )
                throw new IOException("Synthetic difference batch failure before commit.");
            return Task.CompletedTask;
        }
    }
}
