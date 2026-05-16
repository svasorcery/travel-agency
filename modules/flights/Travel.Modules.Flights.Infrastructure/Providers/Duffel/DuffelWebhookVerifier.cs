using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

/// <summary>
/// Verifies the <c>X-Duffel-Signature</c> header on incoming Duffel webhooks.
/// </summary>
/// <remarks>
/// Scheme (verified against the Duffel "Receiving webhooks" guide,
/// <see href="https://duffel.com/docs/guides/receiving-webhooks"/>):
/// <list type="bullet">
///   <item>Header value is a comma-separated list of <c>k=v</c> pairs.
///         Currently <c>t=&lt;unix-seconds&gt;,v1=&lt;hex&gt;</c>, e.g.
///         <c>t=1616202842,v1=8aebaa7ecaf36950721e4321b6a56d7493d13e73814de672ac5ce4ddd7435054</c>.</item>
///   <item>The signed payload is the bytes of the timestamp string, then a single
///         <c>.</c> byte, then the raw request body bytes: <c>timestamp.encode() + "." + payload</c>.</item>
///   <item>The signature is HMAC-SHA256 of that payload under the webhook secret,
///         encoded as lowercase base16 (hex).</item>
///   <item>The Duffel guide does not specify a timestamp tolerance window. We accept
///         any well-formed timestamp; replay-window enforcement is intentionally not
///         implemented here (the inbox de-dup by event id is the replay defence).</item>
/// </list>
/// Constant-time comparison is used to avoid timing side channels.
/// </remarks>
public sealed class DuffelWebhookVerifier(IOptions<DuffelOptions> opts)
{
    public bool Verify(byte[] body, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;
        if (string.IsNullOrEmpty(opts.Value.WebhookSecret))
            return false;

        if (!TryParseHeader(signatureHeader, out var timestamp, out var v1Hex))
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(v1Hex);
        }
        catch (FormatException)
        {
            return false;
        }

        // Build signed payload: "<timestamp>.<body>" as a single byte buffer.
        var tsBytes = Encoding.UTF8.GetBytes(timestamp);
        var signed = new byte[tsBytes.Length + 1 + body.Length];
        Buffer.BlockCopy(tsBytes, 0, signed, 0, tsBytes.Length);
        signed[tsBytes.Length] = (byte)'.';
        Buffer.BlockCopy(body, 0, signed, tsBytes.Length + 1, body.Length);

        var key = Encoding.UTF8.GetBytes(opts.Value.WebhookSecret);
        var computed = HMACSHA256.HashData(key, signed);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>
    /// Parses a header of the form <c>t=&lt;ts&gt;,v1=&lt;hex&gt;</c>. Order of fields is
    /// not assumed. Returns <c>false</c> for missing/duplicate/malformed fields. Both
    /// <c>t</c> and <c>v1</c> must be present and non-empty.
    /// </summary>
    private static bool TryParseHeader(string header, out string timestamp, out string v1Hex)
    {
        timestamp = string.Empty;
        v1Hex = string.Empty;

        foreach (var part in header.Split(','))
        {
            var trimmed = part.Trim();
            var eq = trimmed.IndexOf('=');
            if (eq <= 0 || eq == trimmed.Length - 1)
                return false;

            var key = trimmed[..eq];
            var value = trimmed[(eq + 1)..];

            switch (key)
            {
                case "t":
                    if (timestamp.Length != 0)
                        return false;
                    timestamp = value;
                    break;
                case "v1":
                    if (v1Hex.Length != 0)
                        return false;
                    v1Hex = value;
                    break;
                // Forward-compatible: unknown fields are ignored, not rejected.
            }
        }

        return timestamp.Length > 0 && v1Hex.Length > 0;
    }
}
