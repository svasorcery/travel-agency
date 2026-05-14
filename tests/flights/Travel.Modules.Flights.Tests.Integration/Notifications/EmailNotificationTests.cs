using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

/// <summary>
/// Uses real RazorLight renderer (exercises template compilation) with a recording fake sender
/// and a fake user directory over Testcontainers Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EmailNotificationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;
    private OrderReadModelQueries _queries = default!;
    private RazorLightEmailRenderer _renderer = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        var options = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new FlightsDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _queries = new OrderReadModelQueries(_db);
        _renderer = new RazorLightEmailRenderer();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid AggregateId, Guid UserId)> SeedOrderAsync(string currency = "RUB")
    {
        var aggId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _db.Orders.Add(
            new OrderReadModelEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = aggId,
                UserId = userId,
                ProviderOrderId = "ord_test",
                Status = "Confirmed",
                TotalAmount = 12500m,
                Currency = currency,
                ItineraryJson = """{"origin":"SVO","destination":"LED"}""",
                PassengerInfoJson = "null",
                TicketNumbers = Array.Empty<string>(),
                BookedAt = DateTimeOffset.UtcNow,
            }
        );
        await _db.SaveChangesAsync();
        return (aggId, userId);
    }

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendOrderConfirmationEmail_RuLocale_SendsEmailWithCorrectContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aggId, userId) = await SeedOrderAsync();

        var fakeSender = new RecordingEmailSender();
        var fakeUsers = new FakeUserDirectory(
            new UserProfile(userId, "ivan@example.com", "Иван", "Петров", "ru")
        );

        var notification = new Application.Contracts.OrderConfirmedNotification(aggId, userId);

        await SendOrderConfirmationEmailHandler.Handle(
            notification,
            _queries,
            fakeSender,
            _renderer,
            fakeUsers,
            ct
        );

        fakeSender.Sent.Count.ShouldBe(1);
        var sent = fakeSender.Sent[0];
        sent.Subject.ShouldContain("подтверждено", Case.Insensitive);
        sent.HtmlBody.ShouldContain(aggId.ToString("N")[..6].ToUpperInvariant()); // BookingRef
        sent.HtmlBody.ShouldContain("Иван");
        sent.ToEmail.ShouldBe("ivan@example.com");
    }

    [Fact]
    public async Task SendOrderConfirmationEmail_EnLocale_SendsEmailWithCorrectContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aggId, userId) = await SeedOrderAsync();

        var fakeSender = new RecordingEmailSender();
        var fakeUsers = new FakeUserDirectory(
            new UserProfile(userId, "john@example.com", "John", "Smith", "en")
        );

        var notification = new Application.Contracts.OrderConfirmedNotification(aggId, userId);

        await SendOrderConfirmationEmailHandler.Handle(
            notification,
            _queries,
            fakeSender,
            _renderer,
            fakeUsers,
            ct
        );

        fakeSender.Sent.Count.ShouldBe(1);
        var sent = fakeSender.Sent[0];
        sent.Subject.ShouldContain("confirmed", Case.Insensitive);
        sent.HtmlBody.ShouldContain(aggId.ToString("N")[..6].ToUpperInvariant());
        sent.HtmlBody.ShouldContain("John");
    }

    [Fact]
    public async Task SendOrderCancellationEmail_RuLocale_SendsEmailWithCorrectContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aggId, userId) = await SeedOrderAsync();

        var fakeSender = new RecordingEmailSender();
        var fakeUsers = new FakeUserDirectory(
            new UserProfile(userId, "ivan@example.com", "Иван", "Петров", "ru")
        );

        var notification = new Application.Contracts.OrderCancelledNotification(aggId, userId);

        await SendOrderCancellationEmailHandler.Handle(
            notification,
            _queries,
            fakeSender,
            _renderer,
            fakeUsers,
            ct
        );

        fakeSender.Sent.Count.ShouldBe(1);
        var sent = fakeSender.Sent[0];
        sent.Subject.ShouldContain("отменено", Case.Insensitive);
        sent.HtmlBody.ShouldContain(aggId.ToString("N")[..6].ToUpperInvariant());
        sent.HtmlBody.ShouldContain("Иван");
    }
}

// ─── test doubles ───────────────────────────────────────────────────────────────

file sealed record CapturedEmail(string ToEmail, string Subject, string HtmlBody);

file sealed class RecordingEmailSender : IEmailSender
{
    public List<CapturedEmail> Sent { get; } = [];

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        Sent.Add(new CapturedEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}

file sealed class FakeUserDirectory(UserProfile profile) : IUserDirectory
{
    public Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<UserProfile?>(profile.Id == userId ? profile : null);
}
