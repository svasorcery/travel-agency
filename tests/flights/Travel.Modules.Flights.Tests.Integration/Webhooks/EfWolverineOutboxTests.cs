using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

/// <summary>
/// Proves that <see cref="FlightsDbContext"/>, once Wolverine is made aware of it via
/// <c>UseEntityFrameworkCoreTransactions()</c>, behaves as a transactional outbox: a
/// <c>[Transactional]</c> handler that inserts a <c>WebhookInboxEntity</c> AND publishes
/// <c>ProcessDuffelWebhookCommand</c> commits the DbContext save and the outgoing message
/// in one transaction, and the command is then delivered.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EfWolverineOutboxTests : IClassFixture<WolverineOutboxFixture>
{
    private readonly WolverineOutboxFixture _fixture;

    public EfWolverineOutboxTests(WolverineOutboxFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FlightsDbContext_acts_as_transactional_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var inboxId = Guid.NewGuid();
        var eventId = "wh_" + Guid.NewGuid().ToString("N");

        // Drive the [Transactional] handler: it inserts the inbox row and publishes
        // ProcessDuffelWebhookCommand through the EF-enrolled outbox. TrackActivity waits
        // until the resulting command has been delivered to its handler.
        await _fixture
            .Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .InvokeMessageAndWaitAsync(new EfOutboxProbeCommand(inboxId, eventId));

        // The inbox row committed.
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var row = await db.WebhookInbox.FirstOrDefaultAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        row.ShouldNotBeNull();
        row.Id.ShouldBe(inboxId);

        // The outgoing command rode the outbox and was delivered.
        _fixture.Probe.WasHandled(inboxId).ShouldBeTrue();
    }
}
