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

    private sealed class EmailBoundary(string locale)
        : IBookingNotificationReadiness,
            IEmailSender,
            IEmailRenderer,
            IUserDirectory
    {
        public string? Text { get; private set; }

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
                    "{}",
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
