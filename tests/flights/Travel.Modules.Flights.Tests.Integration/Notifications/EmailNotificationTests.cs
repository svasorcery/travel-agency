using System.Globalization;
using JasperFx.Events;
using Marten.Events;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

/// <summary>
/// Uses the real <see cref="HtmlTemplateEmailRenderer"/> (exercises file-system template
/// loading and token replacement) with a recording fake sender and a fake user directory
/// over Testcontainers Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EmailNotificationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;
    private OrderReadModelQueries _queries = default!;
    private HtmlTemplateEmailRenderer _renderer = default!;

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
        _renderer = new HtmlTemplateEmailRenderer();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid AggregateId, Guid UserId)> SeedOrderAsync(
        string currency = "RUB",
        string status = "Confirmed"
    )
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
                Status = status,
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Real_projected_itinerary_renders_every_leg_in_confirmation_and_cancellation(
        int legs
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var routes = new[] { ("SVO", "LED"), ("DME", "KZN"), ("KUF", "OVB"), ("OVB", "KJA") };
        var now = DateTimeOffset.UtcNow;
        var slices = routes
            .Take(legs)
            .Select(
                (route, index) =>
                    Slice
                        .Create([
                            Segment
                                .Create(
                                    IataCode.Create(route.Item1).Value,
                                    IataCode.Create(route.Item2).Value,
                                    now.AddDays(index + 1),
                                    now.AddDays(index + 1).AddHours(2),
                                    "SU",
                                    "100",
                                    CabinClass.Economy
                                )
                                .Value,
                        ])
                        .Value
            )
            .ToArray();
        var itinerary = Itinerary.Create(slices).Value;
        var row = new OrderReadModelEntity
        {
            Id = Guid.NewGuid(),
            AggregateId = id,
            ProjectedStreamVersion = 0,
        };
        long version = 0;
        void Apply<T>(T data)
            where T : notnull =>
            OrderReadModelEventApplier.Apply(
                row,
                new Event<T>(data) { StreamId = id, Version = ++version }
            );
        Apply(
            new OfferQuoted(
                OfferId.New(),
                itinerary,
                BookingReconcilerFixture.Amount,
                now.AddHours(1),
                "off-fictional",
                now
            )
        );
        Apply(BookingReconcilerFixture.Held(owner));
        Apply(new OrderConfirmed("ord-fictional", PaymentRef.New(), now));
        _db.Orders.Add(row);
        await _db.SaveChangesAsync(ct);
        var sender = new RecordingEmailSender();
        var users = new FakeUserDirectory(
            new UserProfile(owner, "fictional@example.test", "Test", "Traveler", "en")
        );
        await SendOrderConfirmationEmailHandler.Handle(
            new(id, owner, version),
            new EmailTestReadiness(_queries),
            sender,
            _renderer,
            users,
            ct
        );
        Apply(new OrderCancelled(CancelReason.User, now));
        await _db.SaveChangesAsync(ct);
        await SendOrderCancellationEmailHandler.Handle(
            new(id, owner, CancelReason.User, version),
            new EmailTestReadiness(_queries),
            sender,
            _renderer,
            users,
            ct
        );
        sender.Sent.Count.ShouldBe(2);
        var expected = string.Join(
            "; ",
            routes.Take(legs).Select(route => $"{route.Item1} → {route.Item2}")
        );
        foreach (var sent in sender.Sent)
        {
            var html = System.Net.WebUtility.HtmlDecode(sent.HtmlBody);
            html.ShouldContain(expected);
            html.ShouldNotContain("\"Slices\"");
            html.ShouldNotContain("\"Value\"");
        }
    }

    [Fact]
    public async Task Legacy_routes_remain_readable_and_unknown_shapes_never_echo_technical_json()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, owner) = await SeedOrderAsync();
        var row = await _db.Orders.SingleAsync(x => x.AggregateId == id, ct);
        var users = new FakeUserDirectory(
            new UserProfile(owner, "fictional@example.test", "Test", "Traveler", "en")
        );
        foreach (
            var (json, expected) in new[]
            {
                ("{\"origin\":\"SVO\",\"destination\":\"LED\"}", "SVO → LED"),
                (
                    "{\"slices\":[{\"origin\":\"SVO\",\"destination\":\"LED\"},{\"origin\":\"DME\",\"destination\":\"KZN\"}]}",
                    "SVO → LED; DME → KZN"
                ),
                ("{\"unexpected\":\"private-json-sentinel\"}", "Itinerary unavailable"),
                ("null", "Itinerary unavailable"),
            }
        )
        {
            row.ItineraryJson = json;
            await _db.SaveChangesAsync(ct);
            var sender = new RecordingEmailSender();
            await SendOrderConfirmationEmailHandler.Handle(
                new(id, owner),
                new EmailTestReadiness(_queries),
                sender,
                _renderer,
                users,
                ct
            );
            var html = System.Net.WebUtility.HtmlDecode(
                sender.Sent.ShouldHaveSingleItem().HtmlBody
            );
            html.ShouldContain(expected);
            html.ShouldNotContain("private-json-sentinel");
        }
    }

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
            new EmailTestReadiness(_queries),
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
            new EmailTestReadiness(_queries),
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
        var (aggId, userId) = await SeedOrderAsync(status: "Cancelled");

        var fakeSender = new RecordingEmailSender();
        var fakeUsers = new FakeUserDirectory(
            new UserProfile(userId, "ivan@example.com", "Иван", "Петров", "ru")
        );

        var notification = new Application.Contracts.OrderCancelledNotification(aggId, userId);

        await SendOrderCancellationEmailHandler.Handle(
            notification,
            new EmailTestReadiness(_queries),
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

    [Fact]
    public async Task Cancellation_email_includes_reason_and_refund_text()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aggId, userId) = await SeedOrderAsync(status: "Cancelled");

        var fakeSender = new RecordingEmailSender();
        var fakeUsers = new FakeUserDirectory(
            new UserProfile(userId, "ivan@example.com", "Иван", "Петров", "ru")
        );

        // Pass reason explicitly
        var notification = new Application.Contracts.OrderCancelledNotification(
            aggId,
            userId,
            Travel.Modules.Flights.Core.DomainEvents.CancelReason.User
        );

        await SendOrderCancellationEmailHandler.Handle(
            notification,
            new EmailTestReadiness(_queries),
            fakeSender,
            _renderer,
            fakeUsers,
            ct
        );

        fakeSender.Sent.Count.ShouldBe(1);
        var sent = fakeSender.Sent[0];
        // Template must include cancel reason and refund expectation text
        sent.HtmlBody.ShouldContain(
            "причина",
            Case.Insensitive,
            "Expected cancel reason token in RU template"
        );
        sent.HtmlBody.ShouldContain(
            "возврат",
            Case.Insensitive,
            "Expected refund text in RU template"
        );
        sent.HtmlBody.ShouldContain("Отмена заказа не подтверждает возврат средств");
        sent.HtmlBody.ShouldNotContain("5–10");
    }

    [Fact]
    public async Task Confirmation_email_includes_ticket_follows_disclaimer()
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
            new EmailTestReadiness(_queries),
            fakeSender,
            _renderer,
            fakeUsers,
            ct
        );

        fakeSender.Sent.Count.ShouldBe(1);
        var sent = fakeSender.Sent[0];
        // Template must include a "ticket follows" disclaimer
        sent.HtmlBody.ShouldContain(
            "ticket",
            Case.Insensitive,
            "Expected ticket-follows disclaimer in EN confirmation template"
        );
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

file sealed class EmailTestReadiness(IOrderReadModelQueries queries) : IBookingNotificationReadiness
{
    public async Task<OrderView> RequireAsync(
        Guid aggregateId,
        Guid userId,
        long? requiredStreamVersion,
        CancellationToken ct
    ) =>
        await queries.GetAsync(aggregateId, userId, ct)
        ?? throw new InvalidOperationException("Test order missing");
}
