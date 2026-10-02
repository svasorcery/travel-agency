using System.Text.Json;
using MailKit.Net.Smtp;

namespace Travel.Modules.Flights.Infrastructure.Privacy;

internal static class PrivacySafeFailure
{
    // Preserve cancellation and common transport categories, never the sensitive inner chain.
    public static Exception From(Exception error, string safeMessage) =>
        error switch
        {
            TaskCanceledException cancelled => new TaskCanceledException(
                safeMessage,
                null,
                cancelled.CancellationToken
            ),
            OperationCanceledException cancelled => new OperationCanceledException(
                safeMessage,
                cancelled.CancellationToken
            ),
            SmtpCommandException smtp => new SmtpCommandException(
                smtp.ErrorCode,
                smtp.StatusCode,
                safeMessage
            ),
            SmtpProtocolException => new SmtpProtocolException(safeMessage),
            HttpRequestException http => new HttpRequestException(
                safeMessage,
                null,
                http.StatusCode
            ),
            JsonException => new JsonException(safeMessage),
            TimeoutException => new TimeoutException(safeMessage),
            IOException => new IOException(safeMessage),
            _ => new InvalidOperationException(safeMessage),
        };
}
