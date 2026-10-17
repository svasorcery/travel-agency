using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Host.Configuration;
using Travel.Modules.Hotels.Api.Middleware;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;
using Xunit;

namespace Travel.Host.Tests.Integration.Hotels;

public sealed class HotelsTransportBoundsTests
{
    [Theory]
    [InlineData(true, 400)]
    [InlineData(false, 503)]
    public async Task Required_owner_payload_failure_differs_from_an_unrelated_receiver_failure(
        bool requiredOwnerInvalid,
        int expected
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IRawWebhookReceiver>(
            new ProbeReceiver("flights", new(RawWebhookProbeKind.RequiredReceiver, "hotels"))
        );
        builder.Services.AddSingleton<IRawWebhookReceiver>(
            new ProbeReceiver(
                "hotels",
                new(
                    requiredOwnerInvalid
                        ? RawWebhookProbeKind.InvalidPayload
                        : RawWebhookProbeKind.Unavailable
                )
            )
        );
        if (!requiredOwnerInvalid)
            builder.Services.AddSingleton<IRawWebhookReceiver>(
                new ProbeReceiver("unrelated", new(RawWebhookProbeKind.InvalidPayload))
            );
        builder.Services.AddSingleton<IOptions<RawWebhookTransportOptions>>(
            Options.Create(new RawWebhookTransportOptions())
        );
        await using var app = builder.Build();
        app.MapPost("/webhooks/duffel", Travel.Host.Endpoints.DuffelWebhookEndpoint.Receive);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/duffel")
        {
            Content = new ByteArrayContent([1]),
        };
        request.Headers.Add("X-Duffel-Signature", "fictional-signature");
        using var response = await app.GetTestClient()
            .SendAsync(request, TestContext.Current.CancellationToken);
        ((int)response.StatusCode).ShouldBe(expected);
    }

    private sealed class ProbeReceiver(string name, RawWebhookProbe probe) : IRawWebhookReceiver
    {
        public string Name => name;

        public RawWebhookProbe Probe(RawWebhookDelivery delivery) => probe;

        public Task<RawWebhookAcceptance> AcceptAsync(
            RawWebhookDelivery delivery,
            CancellationToken ct
        ) => throw new InvalidOperationException("No unique owner may write.");
    }

    [Fact]
    public async Task Tightened_hotel_body_limit_refuses_before_binding_and_keeps_no_store()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IOptions<HotelRequestOptions>>(
            Options.Create(new HotelRequestOptions { RequestBytes = 256 })
        );
        await using var app = builder.Build();
        app.UseMiddleware<HotelsRequestPolicyMiddleware>();
        var entered = 0;
        app.MapPost(
            "/api/hotels/guard",
            () =>
            {
                entered++;
                return "unexpected";
            }
        );
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var response = await app.GetTestClient()
            .PostAsync(
                "/api/hotels/guard",
                new ByteArrayContent(new byte[257]),
                TestContext.Current.CancellationToken
            );
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        entered.ShouldBe(0);
        new HotelRequestOptions().RequestBytes.ShouldBe(16384);
        new HotelRequestOptions { RequestBytes = 16385 }
            .IsValid()
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(257, 1, false, 413, 0)]
    [InlineData(256, 1, false, 200, 1)]
    [InlineData(256, 2, false, 503, 0)]
    [InlineData(256, 1, true, 401, 0)]
    public async Task Raw_route_keeps_exact_bytes_unique_owner_and_validated_tightened_limit(
        int bytes,
        int owners,
        bool repeatedSignature,
        int expected,
        int accepted
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var receivers = Enumerable.Range(0, owners).Select(_ => new RecordingReceiver()).ToArray();
        foreach (var receiver in receivers)
            builder.Services.AddSingleton<IRawWebhookReceiver>(receiver);
        builder.Services.AddSingleton<IOptions<RawWebhookTransportOptions>>(
            Options.Create(new RawWebhookTransportOptions { MaximumBytes = 256 })
        );
        await using var app = builder.Build();
        app.MapPost("/webhooks/duffel", Travel.Host.Endpoints.DuffelWebhookEndpoint.Receive);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/duffel")
        {
            Content = new ByteArrayContent(
                Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray()
            ),
        };
        request.Headers.Add(
            "X-Duffel-Signature",
            repeatedSignature
                ? new[] { "fictional-signature", "fictional-signature" }
                : ["fictional-signature"]
        );
        using var result = await app.GetTestClient()
            .SendAsync(request, TestContext.Current.CancellationToken);
        ((int)result.StatusCode).ShouldBe(expected);
        receivers.Sum(r => r.Accepted).ShouldBe(accepted);
        if (repeatedSignature)
        {
            using var problem = System.Text.Json.JsonDocument.Parse(
                await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            problem
                .RootElement.GetProperty("type")
                .GetString()
                .ShouldBe("https://travel.local/errors/Flights.Webhook.InvalidSignature");
            problem.RootElement.GetProperty("title").GetString().ShouldBe("Unauthorized");
        }
        if (accepted > 0)
            receivers[0].Bytes.ShouldBe(Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());
        if (bytes > 256 || repeatedSignature)
            receivers.Sum(r => r.Probed).ShouldBe(0);
        new RawWebhookTransportOptions().MaximumBytes.ShouldBe(2 * 1024 * 1024);
        new RawWebhookTransportOptions { MaximumBytes = 2 * 1024 * 1024 + 1 }
            .IsValid()
            .ShouldBeFalse();
    }

    private sealed class RecordingReceiver : IRawWebhookReceiver
    {
        public string Name => "fictional";
        public int Probed { get; private set; }
        public int Accepted { get; private set; }
        public byte[] Bytes { get; private set; } = [];

        public RawWebhookProbe Probe(RawWebhookDelivery delivery)
        {
            Probed++;
            return new(RawWebhookProbeKind.Owner);
        }

        public Task<RawWebhookAcceptance> AcceptAsync(
            RawWebhookDelivery delivery,
            CancellationToken ct
        )
        {
            Accepted++;
            Bytes = delivery.Bytes.ToArray();
            return Task.FromResult(RawWebhookAcceptance.Accepted);
        }
    }
}
