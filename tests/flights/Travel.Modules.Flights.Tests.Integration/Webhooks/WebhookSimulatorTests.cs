using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

/// <summary>
/// Verifies that WebhookSimulatorFactory (spec §17.2) produces a payload whose
/// X-Duffel-Signature header is accepted by the real DuffelWebhookVerifier from WS3.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WebhookSimulatorTests
{
    private const string TestSecret = "test-webhook-secret-32-chars-long!";

    private DuffelWebhookVerifier CreateVerifier() =>
        new DuffelWebhookVerifier(Options.Create(new DuffelOptions { WebhookSecret = TestSecret }));

    [Fact]
    public void Simulator_emits_verifiable_signed_webhook()
    {
        // Arrange
        var (payload, signature) = WebhookSimulatorFactory.BuildSignedWebhook(
            orderId: "ord_test_0001",
            eventType: "order.created.documents_issued",
            ticketNumber: "TKT-9999",
            webhookSecret: TestSecret
        );

        // Act
        var body = System.Text.Encoding.UTF8.GetBytes(payload);
        var verifier = CreateVerifier();
        var result = verifier.Verify(body, signature);

        // Assert
        result.ShouldBeTrue(
            $"DuffelWebhookVerifier rejected the simulator's signature. "
                + $"Signature={signature}, Payload={payload}"
        );
    }

    [Fact]
    public void Simulator_signature_is_rejected_for_wrong_secret()
    {
        // Verifies that a signature produced with a different secret does not verify.
        var (payload, signature) = WebhookSimulatorFactory.BuildSignedWebhook(
            orderId: "ord_test_0002",
            eventType: "order.created.documents_issued",
            ticketNumber: "TKT-0001",
            webhookSecret: "wrong-secret"
        );

        var body = System.Text.Encoding.UTF8.GetBytes(payload);
        var verifier = CreateVerifier();
        verifier.Verify(body, signature).ShouldBeFalse();
    }
}
