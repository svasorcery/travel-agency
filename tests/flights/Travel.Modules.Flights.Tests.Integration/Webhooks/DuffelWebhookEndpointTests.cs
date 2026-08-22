using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Shouldly;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Webhooks;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

public sealed class DuffelWebhookEndpointTests
{
    [Theory]
    [InlineData(WebhookIngestionOutcome.Accepted)]
    [InlineData(WebhookIngestionOutcome.Duplicate)]
    public async Task Accepted_and_duplicate_outcomes_return_ok(WebhookIngestionOutcome outcome)
    {
        var service = new RecordingWebhookIngestionService(_ => outcome);
        var request = BuildRequest(new byte[] { 0x01 });

        var result = await DuffelWebhookEndpoint.Receive(
            request,
            service,
            TestContext.Current.CancellationToken
        );

        result.ShouldBeOfType<Ok>();
        service.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Raw_payload_and_headers_are_snapshotted_without_transport_changes()
    {
        var payload = new byte[] { 0x00, 0x7F, 0x80, 0xFF };
        var service = new RecordingWebhookIngestionService(_ => WebhookIngestionOutcome.Accepted);
        var request = BuildRequest(payload);
        request.Headers["x-DuFfEl-SiGnAtUrE"] = "t=1,v1=abc";

        await DuffelWebhookEndpoint.Receive(
            request,
            service,
            TestContext.Current.CancellationToken
        );

        var captured = service.Request.ShouldNotBeNull();
        captured.Provider.ShouldBe("duffel");
        captured.Payload.ToArray().ShouldBe(payload);
        captured.Headers["X-DUFFEL-SIGNATURE"].ShouldBe("t=1,v1=abc");

        var snapshot = captured.Headers.ShouldBeOfType<Dictionary<string, string>>();
        snapshot.Comparer.ShouldBeSameAs(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Multiple_signature_header_values_are_rejected_before_service_invocation()
    {
        var service = new RecordingWebhookIngestionService(_ => WebhookIngestionOutcome.Accepted);
        var request = BuildRequest(new byte[] { 0x01 }, includeSignature: false);
        request.Headers.Append("X-Duffel-Signature", $"t=1700000000,v1={new string('a', 64)}");
        request.Headers.Append("X-Duffel-Signature", "ignored=value");

        var result = await DuffelWebhookEndpoint.Receive(
            request,
            service,
            TestContext.Current.CancellationToken
        );

        var problem = result.ShouldBeOfType<ProblemHttpResult>().ProblemDetails;
        problem.Status.ShouldBe(StatusCodes.Status401Unauthorized);
        problem.Type.ShouldBe("https://travel.local/errors/Flights.Webhook.InvalidSignature");
        service.CallCount.ShouldBe(0);
        service.Request.ShouldBeNull();
    }

    [Theory]
    [InlineData("signature", StatusCodes.Status401Unauthorized)]
    [InlineData("payload", StatusCodes.Status400BadRequest)]
    public async Task Typed_ingestion_errors_use_shared_rfc7807_mapping(
        string errorKind,
        int expectedStatus
    )
    {
        var error =
            errorKind == "signature"
                ? WebhookIngestionErrors.InvalidSignature
                : WebhookIngestionErrors.InvalidPayload;
        var service = new RecordingWebhookIngestionService(_ => error);
        var request = BuildRequest(new byte[] { 0x01 });

        var result = await DuffelWebhookEndpoint.Receive(
            request,
            service,
            TestContext.Current.CancellationToken
        );

        var problem = result.ShouldBeOfType<ProblemHttpResult>().ProblemDetails;
        problem.Status.ShouldBe(expectedStatus);
        problem.Type.ShouldBe($"https://travel.local/errors/{error.Code}");
        var detail = problem.Detail.ShouldNotBeNull();
        detail.ShouldBe(error.Description);
        detail.ShouldNotContain("DbContext", Case.Insensitive);
        detail.ShouldNotContain("DuffelWebhookEventDto", Case.Insensitive);
    }

    private static HttpRequest BuildRequest(byte[] payload, bool includeSignature = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(payload);
        if (includeSignature)
            context.Request.Headers["X-Duffel-Signature"] = "t=1700000000,v1=abc";

        return context.Request;
    }

    private sealed class RecordingWebhookIngestionService(
        Func<WebhookIngestionRequest, ErrorOr<WebhookIngestionOutcome>> result
    ) : IWebhookIngestionService
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
            return Task.FromResult(result(request));
        }
    }
}
