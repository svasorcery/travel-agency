using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Privacy;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Email;

public sealed class MailKitEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<MailKitEmailSender> logger
) : IEmailSender
{
    private readonly SmtpOptions settings = options.Value;

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        try
        {
            using var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody,
            }.ToMessageBody();
            using var client = new SmtpClient();
            await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.None, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("Email delivery completed.");
        }
        catch (Exception ex)
        {
            logger.LogError("Email delivery failed ({ErrorType}).", ex.GetType().Name);
            throw PrivacySafeFailure.From(ex, "Email delivery failed.");
        }
    }
}
