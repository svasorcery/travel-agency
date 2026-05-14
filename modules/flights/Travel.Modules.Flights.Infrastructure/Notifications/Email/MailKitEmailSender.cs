using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Email;

public sealed class MailKitEmailSender(
    IConfiguration configuration,
    ILogger<MailKitEmailSender> logger
) : IEmailSender
{
    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct
    )
    {
        var host = configuration["Flights:Smtp:Host"] ?? "localhost";
        var port = int.TryParse(configuration["Flights:Smtp:Port"], out var p) ? p : 1025;
        var fromAddress = configuration["Flights:Smtp:FromAddress"] ?? "noreply@travel.example";
        var fromName = configuration["Flights:Smtp:FromName"] ?? "Travel Platform";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody };
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            // Mailpit and local dev SMTP typically don't require TLS or auth
            await client.ConnectAsync(host, port, SecureSocketOptions.None, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation(
                "Email '{Subject}' sent to {ToEmail} via {Host}:{Port}",
                subject,
                toEmail,
                host,
                port
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send email '{Subject}' to {ToEmail} via {Host}:{Port}",
                subject,
                toEmail,
                host,
                port
            );
            throw;
        }
    }
}
