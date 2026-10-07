using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Ancillaries;

// Actual durable-inbox/host replacement execution belongs to existing CI only.
[Trait("Category", "Integration")]
public sealed class BookingCreationRestartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Persisted_scheduled_check_survives_host_replacement_and_redelivery_never_creates()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var provider = new Reader();
        var id = Guid.NewGuid();
        Guid attemptId;
        using (var first = await Start(postgres.GetConnectionString(), provider, false))
        {
            using var scope = first.Services.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var outbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
            var now = TimeProvider.System.GetUtcNow();
            var startedAt = now.AddSeconds(-151);
            var money = Money.Create(50, CurrencyCode.Create("GBP").Value).Value;
            var segment = Segment
                .Create(
                    IataCode.Create("LHR").Value,
                    IataCode.Create("CDG").Value,
                    now.AddDays(2),
                    now.AddDays(2).AddHours(1),
                    "BA",
                    "101",
                    CabinClass.Economy
                )
                .Value;
            var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
            var reference = SupplierPassengerReference.Create("pas_fictional").Value;
            var party = BookableOfferParty
                .Create(
                    [new(reference, BookingPassengerKind.Adult)],
                    DateOnly.FromDateTime(segment.DepartAt.Date),
                    true,
                    false
                )
                .Value;
            var binding = QuoteBinding
                .Create(
                    Guid.NewGuid(),
                    party,
                    [
                        new(
                            BookingPassengerId.Create(Guid.NewGuid()).Value,
                            reference,
                            BookingPassengerKind.Adult
                        ),
                    ]
                )
                .Value;
            var purchase = BookingPurchase.Empty(binding.Revision, money, now.AddHours(1));
            var owner = Guid.NewGuid();
            attemptId = Guid.NewGuid();
            session.Events.StartStream<BookingAggregate>(
                id,
                new OfferQuoted(
                    OfferId.New(),
                    itinerary,
                    money,
                    purchase.ExpiresAt,
                    "off_fictional",
                    startedAt,
                    new(false, false, null, null),
                    binding
                ),
                new BookingPurchaseQuoted(purchase, startedAt),
                new BookingCreationStarted(
                    attemptId,
                    owner,
                    new string('a', 64),
                    binding.Revision,
                    purchase,
                    ProtectedPassengerPartySnapshot.Create(1, "opaque-party-fixture").Value,
                    Guid.NewGuid(),
                    startedAt
                ),
                new BookingCreationObserved(
                    attemptId,
                    new(
                        BookingCreationOutcome.ManualReviewRequired,
                        null,
                        "ord_fictional",
                        true,
                        true,
                        "PartialReceipt",
                        now
                    ),
                    now
                )
            );
            provider.Actual = new(
                "ord_fictional",
                "off_fictional",
                itinerary,
                new(binding.Slots.Select(s => s.Id.Value).ToArray()),
                new([]),
                money,
                true,
                now.AddHours(2)
            );
            await session.SaveBookingWithWorkAsync(
                outbox,
                id,
                [new(new CheckBookingCreation(id, attemptId), now.AddMilliseconds(300))],
                [],
                Ct
            );
            await first.StopAsync(Ct);
        }
        using var second = await Start(postgres.GetConnectionString(), provider, true);
        var recovered = await Wait(second, id, b => b.CurrentCreation?.CreationCompleted == true);
        recovered.CurrentCreation!.Outcome.ShouldBe(BookingCreationOutcome.Matches);
        recovered.Status.ShouldBe(BookingStatus.Held);
        var bus = second.Services.GetRequiredService<IMessageBus>();
        await bus.InvokeAsync(new CheckBookingCreation(id, attemptId), Ct);
        await bus.InvokeAsync(new CheckBookingCreation(id, attemptId), Ct);
        provider.Reads.ShouldBe(1);
        provider.Creates.ShouldBe(0);
        await second.StopAsync(Ct);
    }

    [Fact]
    public async Task Missing_source_check_moves_to_error_storage_without_creating_or_negative_proof()
    {
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await postgres.StartAsync(Ct);
        var reader = new Reader();
        using var host = await Start(postgres.GetConnectionString(), reader, true);
        var bus = host.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new CheckBookingCreation(Guid.NewGuid(), Guid.NewGuid()));
        var runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        var until = TimeProvider.System.GetUtcNow().AddSeconds(20);
        var found = false;
        while (TimeProvider.System.GetUtcNow() < until)
        {
            var page = await runtime.Storage.DeadLetters.QueryAsync(new() { PageSize = 100 }, Ct);
            if (page.Envelopes.Any(e => e.MessageType == typeof(CheckBookingCreation).FullName))
            {
                found = true;
                break;
            }
            await Task.Delay(100, Ct);
        }
        found.ShouldBeTrue();
        reader.Creates.ShouldBe(0);
        reader.Reads.ShouldBe(0);
        await host.StopAsync(Ct);
    }

    private static async Task<IHost> Start(string connection, Reader provider, bool recovery)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IFlightBookingProvider>(provider);
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(connection);
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.Discovery.IncludeType(typeof(CheckBookingCreationHandler));
            options.Discovery.IncludeType(typeof(CreationReconcileProbe));
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            BookingCreationDeliveryPolicy.Configure(options);
            options.Services.RunWolverineInSoloMode();
            options.Durability.DurabilityAgentEnabled = recovery;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
        });
        var host = builder.Build();
        if (!recovery)
            await host
                .Services.GetRequiredService<IWolverineRuntime>()
                .Storage.Admin.MigrateAsync();
        await host.StartAsync(Ct);
        return host;
    }

    private static async Task<BookingAggregate> Wait(
        IHost host,
        Guid id,
        Func<BookingAggregate, bool> ready
    )
    {
        var until = TimeProvider.System.GetUtcNow().AddSeconds(20);
        while (TimeProvider.System.GetUtcNow() < until)
        {
            await using var session = host
                .Services.GetRequiredService<IDocumentStore>()
                .QuerySession();
            var loaded = await session.Events.AggregateStreamAsync<BookingAggregate>(id, token: Ct);
            if (loaded is not null && ready(loaded))
                return loaded;
            await Task.Delay(100, Ct);
        }
        throw new TimeoutException("Fictional persisted creation did not reach expected state.");
    }

    private sealed class Reader : IFlightBookingProvider
    {
        public BookedOrderFacts Actual = null!;
        public int Reads;
        public int Creates;
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookedOrderFacts>> ReadOrderForBookingAsync(
            string order,
            BookableOffer offer,
            QuoteBinding binding,
            BookingPurchase purchase,
            Guid attempt,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref Reads);
            return Task.FromResult<ErrorOr<BookedOrderFacts>>(Actual);
        }

        public Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> people,
            BookingPurchase purchase,
            Guid attempt,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref Creates);
            throw new InvalidOperationException("Recovery must not create.");
        }

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string reference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string order,
            Money money,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string order,
            PaymentRef payment,
            Money money,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

public static class CreationReconcileProbe
{
    public static void Handle(ReconcileOrderReadModel _) { }
}
