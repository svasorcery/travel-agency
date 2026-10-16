using System.Globalization;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.DomainEvents;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

public sealed class CancellationEmailTextTests
{
    [Theory]
    [InlineData(
        "{\"Slices\":[{\"Origin\":{\"Value\":\"SVO\"},\"Destination\":{\"Value\":\"LED\"}},{\"Origin\":{\"Value\":\"DME\"},\"Destination\":{\"Value\":\"KZN\"}}]}",
        "SVO → LED; DME → KZN"
    )]
    [InlineData(
        "{\"slices\":[{\"origin\":\"SVO\",\"destination\":\"LED\"},{\"origin\":\"DME\",\"destination\":\"KZN\"}]}",
        "SVO → LED; DME → KZN"
    )]
    [InlineData("{\"unexpected\":\"private-json-sentinel\"}", "Itinerary unavailable")]
    [InlineData("null", "Itinerary unavailable")]
    [InlineData("malformed private-json-sentinel", "Itinerary unavailable")]
    public async Task Itinerary_uses_readable_ordered_legs_or_an_explicit_unknown(
        string json,
        string expected
    )
    {
        var boundary = new EmailBoundary("en", json);
        await SendOrderCancellationEmailHandler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), CancelReason.User, 4),
            boundary,
            boundary,
            boundary,
            boundary,
            TestContext.Current.CancellationToken
        );
        boundary.Summary.ShouldNotBeNull();
        boundary.Summary.ShouldBe(expected);
        boundary.Summary.ShouldNotContain("private-json-sentinel");
    }

    [Theory]
    [InlineData(CancelReason.User)]
    [InlineData(CancelReason.Airline)]
    [InlineData(CancelReason.System)]
    public async Task Cancellation_does_not_promise_a_refund(CancelReason reason)
    {
        foreach (var locale in new[] { "ru", "en" })
        {
            var fake = new EmailBoundary(locale);
            await SendOrderCancellationEmailHandler.Handle(
                new OrderCancelledNotification(Guid.NewGuid(), Guid.NewGuid(), reason, 4),
                fake,
                fake,
                fake,
                fake,
                TestContext.Current.CancellationToken
            );
            fake.Text.ShouldBe(
                locale == "ru"
                    ? "Отмена заказа не подтверждает возврат средств"
                    : "Order cancellation does not confirm a refund"
            );
        }
    }

    private sealed class EmailBoundary(string locale, string itinerary = "{}")
        : IBookingNotificationReadiness,
            IEmailSender,
            IEmailRenderer,
            IUserDirectory
    {
        public string? Text { get; private set; }
        public string? Summary { get; private set; }

        public Task<OrderView> RequireAsync(Guid id, Guid user, long? version, CancellationToken ct)
        {
            version.ShouldBe(4);
            return Task.FromResult(
                new OrderView(
                    id,
                    user,
                    "ord_fixture",
                    "Cancelled",
                    100,
                    "RUB",
                    itinerary,
                    [],
                    DateTimeOffset.UnixEpoch,
                    null,
                    DateTimeOffset.UnixEpoch,
                    null,
                    PassengerCount: 1,
                    ProjectedStreamVersion: 4
                )
            );
        }

        public Task<UserProfile?> GetAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<UserProfile?>(new(id, "demo@example.test", "Demo", "Traveler", locale));

        public Task<RenderedEmail> RenderAsync(
            string templateName,
            OrderEmailModel model,
            CultureInfo culture,
            CancellationToken ct
        )
        {
            Summary = model.ItinerarySummary;
            templateName.ShouldBe("OrderCancellation");
            return Task.FromResult(
                new RenderedEmail("fixture", model.RefundText, model.RefundText)
            );
        }

        public Task SendAsync(
            string to,
            string subject,
            string html,
            string text,
            CancellationToken ct
        )
        {
            Text = text;
            return Task.CompletedTask;
        }
    }
}
