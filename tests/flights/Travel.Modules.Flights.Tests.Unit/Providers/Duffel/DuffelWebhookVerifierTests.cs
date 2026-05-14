using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelWebhookVerifierTests
{
    private const string Secret = "test-webhook-secret-32-bytes-ok!";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes(
        """{"type":"order.created","data":{}}"""
    );

    private static DuffelWebhookVerifier CreateVerifier(string secret = Secret)
    {
        var opts = Options.Create(new DuffelOptions { WebhookSecret = secret });
        return new DuffelWebhookVerifier(opts);
    }

    private static string ComputeSignatureHeader(byte[] body, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Verify_valid_signature_returns_true()
    {
        var verifier = CreateVerifier();
        var header = ComputeSignatureHeader(Body, Secret);

        var result = verifier.Verify(Body, header);

        result.ShouldBeTrue();
    }

    [Fact]
    public void Verify_tampered_body_returns_false()
    {
        var verifier = CreateVerifier();
        var header = ComputeSignatureHeader(Body, Secret);
        var tamperedBody = Encoding.UTF8.GetBytes("""{"type":"order.cancelled","data":{}}""");

        var result = verifier.Verify(tamperedBody, header);

        result.ShouldBeFalse();
    }

    [Fact]
    public void Verify_non_hex_header_returns_false()
    {
        var verifier = CreateVerifier();

        var result = verifier.Verify(Body, "sha256=not-valid-hex!!!");

        result.ShouldBeFalse();
    }

    [Fact]
    public void Verify_null_header_returns_false()
    {
        var verifier = CreateVerifier();

        var result = verifier.Verify(Body, null);

        result.ShouldBeFalse();
    }

    [Fact]
    public void Verify_empty_header_returns_false()
    {
        var verifier = CreateVerifier();

        var result = verifier.Verify(Body, "");

        result.ShouldBeFalse();
    }

    [Fact]
    public void Verify_empty_secret_returns_false()
    {
        var verifier = CreateVerifier(secret: "");
        var header = ComputeSignatureHeader(Body, Secret);

        var result = verifier.Verify(Body, header);

        result.ShouldBeFalse();
    }
}
