using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Shouldly;
using Travel.Modules.Rail.Api.Composition;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

[CollectionDefinition("RailTelemetry", DisableParallelization = true)]
public sealed class RailTelemetryCollection;

[Collection("RailTelemetry")]
public sealed class RailOutboundTelemetryTests
{
    private sealed class Spans : BaseProcessor<Activity>
    {
        public readonly List<string> Values = [];

        public override void OnEnd(Activity data)
        {
            if (data.Kind == ActivityKind.Client)
                lock (Values)
                    Values.Add(
                        string.Join("\n", data.TagObjects.Select(x => x.Key + "=" + x.Value))
                            + string.Join(
                                "\n",
                                data.Events.Select(x =>
                                    x.Name
                                    + string.Join(';', x.Tags.Select(y => y.Key + "=" + y.Value))
                                )
                            )
                    );
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public async Task Actual_socket_client_spans_redact_query_key_and_exceptions(string outcome)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var serverCancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        using var callerCancel = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Respond(listener, outcome, received, serverCancel.Token);
        try
        {
            var builder = new HostApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    DisableDefaults = true,
                    EnvironmentName = "Testing",
                    ApplicationName = "RailTelemetry",
                }
            );
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";
            builder.AddServiceDefaults();
            builder.AddRailModule();
            var spans = new Spans();
            builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(spans));
            using var services = builder.Services.BuildServiceProvider();
            _ = services.GetRequiredService<TracerProvider>();
            var client = services
                .GetRequiredService<IHttpClientFactory>()
                .CreateClient("rail-yandex");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/rail-source?from=RAIL-FROM-SECRET&to=RAIL-TO-SECRET&date=RAIL-DATE-SECRET&visible=kept"
            );
            request.Headers.Add("Authorization", "RAIL-FAKE-KEY-SECRET");
            var pending = client.SendAsync(request, callerCancel.Token);
            await received.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (outcome == "cancel")
            {
                callerCancel.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
            }
            else if (outcome == "failure")
                await Should.ThrowAsync<HttpRequestException>(async () => await pending);
            else
            {
                using var response = await pending;
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
            }
            spans.Values.Count.ShouldBeGreaterThan(0);
            var emitted = string.Join('\n', spans.Values);
            emitted.ShouldContain("from=REDACTED");
            emitted.ShouldContain("to=REDACTED");
            emitted.ShouldContain("date=REDACTED");
            emitted.ShouldContain("visible=kept");
            emitted.ShouldNotContain("RAIL-FROM-SECRET");
            emitted.ShouldNotContain("RAIL-TO-SECRET");
            emitted.ShouldNotContain("RAIL-DATE-SECRET");
            emitted.ShouldNotContain("RAIL-FAKE-KEY-SECRET");
        }
        finally
        {
            serverCancel.Cancel();
            listener.Stop();
            try
            {
                await server;
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }
    }

    private static async Task Respond(
        TcpListener listener,
        string outcome,
        TaskCompletionSource received,
        CancellationToken ct
    )
    {
        using var connection = await listener.AcceptTcpClientAsync(ct);
        await using var stream = connection.GetStream();
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0)
                break;
            text.Append(Encoding.ASCII.GetString(buffer, 0, count));
            if (text.Length > 16384)
                throw new InvalidOperationException("Bounded loopback request exceeded.");
        }
        received.TrySetResult();
        if (outcome == "cancel")
        {
            await Task.Delay(Timeout.Infinite, ct);
            return;
        }
        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(
                outcome == "failure"
                    ? "INVALID\r\n\r\n"
                    : "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
            ),
            ct
        );
    }
}
