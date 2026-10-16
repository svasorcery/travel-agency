using System.Net;
using Anthropic;
using Microsoft.Extensions.AI;
using Shouldly;
using Travel.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

public sealed class NlSearchSdkTests
{
    [Fact]
    public async Task Feature_sdk_does_not_retry_fake_server_failure()
    {
        using var handler = new FailureHandler();
        using var http = new HttpClient(handler);
        var options = NlSearchChatClient.CreateClientOptions("fictional-unused-key", http);
        options.MaxRetries.ShouldBe(0);
        using var chat = new AnthropicClient(options).AsIChatClient("fictional-model");
        var failure = await Record.ExceptionAsync(async () =>
        {
            await chat.GetResponseAsync(
                [new(ChatRole.User, "fictional")],
                new ChatOptions { MaxOutputTokens = 512 },
                TestContext.Current.CancellationToken
            );
        });
        failure.ShouldNotBeNull();
        handler.Calls.ShouldBe(1);
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
                        "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"fictional server failure\"}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
