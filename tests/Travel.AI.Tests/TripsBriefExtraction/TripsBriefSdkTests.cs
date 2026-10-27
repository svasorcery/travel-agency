using System.Net;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.AI.Configuration;
using Travel.AI.TripsBriefExtraction;
using Xunit;
using static Travel.AI.Tests.TripsBriefExtraction.TripsBriefExtractorTests;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefSdkTests
{
    [Fact]
    public async Task Feature_sdk_has_zero_retries_using_only_a_fake_http_handler()
    {
        using var handler = new FailureHandler();
        using var http = new HttpClient(handler);
        var options = TripsBriefChatClient.CreateClientOptions("fictional-unused-key", http);
        options.MaxRetries.ShouldBe(0);
        using var chat = new AnthropicClient(options).AsIChatClient("fictional-model");
        (
            await Record.ExceptionAsync(() =>
                chat.GetResponseAsync(
                    [new(ChatRole.User, "fictional")],
                    new ChatOptions { MaxOutputTokens = 1024 },
                    TestContext.Current.CancellationToken
                )
            )
        ).ShouldNotBeNull();
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_wrong_processor_never_resolves_provider_key(bool wrongProcessor)
    {
        var configuration = Enabled();
        configuration.Enabled = wrongProcessor;
        configuration.ProcessorId = wrongProcessor ? "local" : "anthropic";
        var secret = new ForbiddenOptions();
        using var client = new TripsBriefChatClient(secret, configuration);
        (
            await Record.ExceptionAsync(() =>
                client.GetResponseAsync(
                    [new(ChatRole.User, "fictional")],
                    new ChatOptions { ModelId = "fictional-model" },
                    TestContext.Current.CancellationToken
                )
            )
        ).ShouldBeOfType<InvalidOperationException>();
        secret.Reads.ShouldBe(0);
    }

    private sealed class ForbiddenOptions : IOptions<AnthropicOptions>
    {
        public int Reads;
        public AnthropicOptions Value
        {
            get
            {
                Reads++;
                throw new InvalidOperationException("No credentials authorized.");
            }
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(
                        "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"fictional failure\"}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
