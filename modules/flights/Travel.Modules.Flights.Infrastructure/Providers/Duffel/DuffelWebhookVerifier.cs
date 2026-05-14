using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelWebhookVerifier(IOptions<DuffelOptions> opts)
{
    public bool Verify(byte[] body, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;
        if (string.IsNullOrEmpty(opts.Value.WebhookSecret))
            return false;

        var key = Encoding.UTF8.GetBytes(opts.Value.WebhookSecret);
        var computed = HMACSHA256.HashData(key, body);

        var hex = signatureHeader.Replace("sha256=", "", StringComparison.OrdinalIgnoreCase).Trim();
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
