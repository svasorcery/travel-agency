using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Email;

public sealed class MailKitEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<MailKitEmailSender> logger
) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            // Mailpit and local dev SMTP typically don't require TLS or auth
            await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.None, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation(
                "Email '{Subject}' sent to {ToEmail} via {Host}:{Port}",
                subject,
                toEmail,
                _options.Host,
                _options.Port
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {ToEmail} via {Host}:{Port}",
                subject,
                toEmail,
                _options.Host,
                _options.Port
            );
            throw;
        }
    }
}
