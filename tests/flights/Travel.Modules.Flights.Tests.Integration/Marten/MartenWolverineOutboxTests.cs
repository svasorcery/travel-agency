using Marten;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Marten;

/// <summary>
/// Proves that the Marten document session, once enrolled via
/// <c>AddMarten(...).IntegrateWithWolverine()</c>, behaves as a Wolverine transactional
/// outbox: appending a domain event to a <see cref="BookingAggregate"/> stream AND
/// enqueueing an outgoing message in the SAME <c>SaveChangesAsync</c> commits both
/// atomically and the message is then delivered by the durability agent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MartenWolverineOutboxTests : IClassFixture<WolverineOutboxFixture>
{
    private readonly WolverineOutboxFixture _fixture;

    public MartenWolverineOutboxTests(WolverineOutboxFixture fixture) => _fixture = fixture;

    private static OfferQuoted BuildOfferQuoted()
    {
        var iata = (string c) => IataCode.Create(c).Value;
        var rub = CurrencyCode.Create("RUB").Value;
        var seg = Segment
            .Create(
                iata("LED"),
                iata("DME"),
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary.Create([Slice.Create([seg]).Value]).Value;
        return new OfferQuoted(
            OfferId.New(),
            itinerary,
            Money.Create(5420m, rub).Value,
            DateTimeOffset.UtcNow.AddMinutes(20),
            "off_" + Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow
        );
    }

    [Fact]
    public async Task Marten_session_acts_as_transactional_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        var streamId = Guid.NewGuid();

        // Append a BookingAggregate event AND enqueue an outgoing message through the
        // Marten-enrolled session in ONE SaveChangesAsync. The Wolverine testing helper
        // tracks the full activity until the resulting outgoing message is delivered.
        await using (var scope = _fixture.Host.Services.CreateAsyncScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();
            var outbox = scope.ServiceProvider.GetRequiredService<IMartenOutbox>();
            outbox.Enroll(session);

            session.Events.StartStream<BookingAggregate>(streamId, BuildOfferQuoted());
            await outbox.PublishAsync(new OutboxProbeMessage(correlationId));

            await _fixture
                .Host.TrackActivity()
                .Timeout(TimeSpan.FromSeconds(30))
                .ExecuteAndWaitAsync(_ => session.SaveChangesAsync(ct));
        }

        // The outgoing message rode the outbox and was delivered.
        _fixture.Probe.WasHandled(correlationId).ShouldBeTrue();

        // The event was committed to the same transaction — the stream rebuilds.
        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var verifySession = store.LightweightSession();
        var aggregate = await verifySession.Events.AggregateStreamAsync<BookingAggregate>(
            streamId,
            token: ct
        );
        aggregate.ShouldNotBeNull();
        aggregate.Status.ShouldBe(BookingStatus.OfferQuoted);
    }
}
