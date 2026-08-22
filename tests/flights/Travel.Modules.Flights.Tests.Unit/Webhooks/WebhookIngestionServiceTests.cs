using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Webhooks;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Webhooks;

public sealed class WebhookIngestionServiceTests
{
    [Fact]
    public async Task Unsupported_provider_returns_typed_error_without_calling_port()
    {
        var port = new RecordingWebhookIngestionPort(WebhookIngestionOutcome.Accepted);
        var service = new WebhookIngestionService(port);
        var request = new WebhookIngestionRequest(
            "other",
            new byte[] { 0x01 },
            new Dictionary<string, string>()
        );

        var result = await service.IngestAsync(request, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.Webhook.UnsupportedProvider");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        port.CallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(WebhookIngestionOutcome.Accepted)]
    [InlineData(WebhookIngestionOutcome.Duplicate)]
    public async Task Duffel_request_is_forwarded_unchanged(WebhookIngestionOutcome expectedOutcome)
    {
        var payload = new byte[] { 0x00, 0x7F, 0x80, 0xFF };
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-duffel-signature"] = "t=1,v1=abc",
        };
        var request = new WebhookIngestionRequest("duffel", payload, headers);
        var port = new RecordingWebhookIngestionPort(expectedOutcome);
        var service = new WebhookIngestionService(port);

        var result = await service.IngestAsync(request, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(expectedOutcome);
        port.CallCount.ShouldBe(1);
        port.Request.ShouldBeSameAs(request);
        port.Request!.Payload.ToArray().ShouldBe(payload);
        port.Request.Headers.ShouldBeSameAs(headers);
    }

    [Theory]
    [InlineData("Flights.Webhook.InvalidSignature")]
    [InlineData("Flights.Webhook.InvalidPayload")]
    public async Task Port_error_is_returned_unchanged(string errorCode)
    {
        var error = errorCode switch
        {
            "Flights.Webhook.InvalidSignature" => WebhookIngestionErrors.InvalidSignature,
            _ => WebhookIngestionErrors.InvalidPayload,
        };
        var port = new RecordingWebhookIngestionPort(error);
        var service = new WebhookIngestionService(port);
        var request = new WebhookIngestionRequest(
            "duffel",
            new byte[] { 0x01 },
            new Dictionary<string, string>()
        );

        var result = await service.IngestAsync(request, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(error);
        port.CallCount.ShouldBe(1);
    }

    private sealed class RecordingWebhookIngestionPort(ErrorOr<WebhookIngestionOutcome> result)
        : IWebhookIngestionPort
    {
        public int CallCount { get; private set; }

        public WebhookIngestionRequest? Request { get; private set; }

        public Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
            WebhookIngestionRequest request,
            CancellationToken ct
        )
        {
            CallCount++;
            Request = request;
            return Task.FromResult(result);
        }
    }
}
