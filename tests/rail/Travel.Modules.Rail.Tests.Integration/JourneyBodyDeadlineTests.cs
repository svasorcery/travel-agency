using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Rail.Api.Composition;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Travel.Modules.Rail.Infrastructure;
using Xunit;

namespace Travel.Modules.Rail.Tests.Integration;

public sealed class JourneyBodyDeadlineTests
{
    private sealed class DelayedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public readonly TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public readonly TaskCompletionSource Started = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            Started.TrySetResult();
            await Release.Task;
            // Deliberately uncooperative to prove the lease belongs to actual child settlement.
            return Read(buffer.Span);
        }
    }

    private sealed class Terminal(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }
            );
    }

    [Fact]
    public async Task Fast_headers_with_stalled_body_time_out_without_source_timestamp_and_hold_lease_until_body_settles()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-21T00:00:00Z"));
        await using var stream = new DelayedStream(Encoding.UTF8.GetBytes("{}"));
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing" }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Rail:Enabled"] = "true",
                ["Rail:Fictional"] = "true",
                ["Rail:JourneyDetailsEnabled"] = "true",
            }
        );
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.AddRailModule();
        builder.Services.AddSingleton<IRailJourneyRouteProvider>(s => new YandexRailJourneyProvider(
            new(
                new HttpClient(new Terminal(stream)),
                s.GetRequiredService<RailRuntime>(),
                s.GetRequiredService<RailQuotaLedger>(),
                clock
            ),
            clock
        ));
        await using var services = builder.Services.BuildServiceProvider();
        var suggestions = (
            await services
                .GetRequiredService<RailReferenceService>()
                .SuggestAsync("Учебная", TestContext.Current.CancellationToken)
        ).Value;
        var query = new RailRequest(
            StationRef.Create("yandex-rasp", "s1").Value,
            StationRef.Create("yandex-rasp", "s2").Value,
            suggestions.CatalogueGeneration!,
            "2026-10-21",
            "all"
        );
        var snapshot = (
            await services
                .GetRequiredService<RailSearchService>()
                .SearchAsync(query, TestContext.Current.CancellationToken)
        ).Value;
        var journey = snapshot.Items[0];
        var request = new RailJourneyDetailsRequest(
            query,
            snapshot.ProfileEpoch,
            new(journey.Key, journey.Transport, journey.Departure, journey.Arrival)
        );
        var pending = services
            .GetRequiredService<RailJourneyDetailsService>()
            .GetAsync(request, TestContext.Current.CancellationToken);
        await stream.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var reply = (await pending).Value;
        reply.Kind.ShouldBe("failed");
        reply.Stage.ShouldBe("route");
        reply.RouteReceivedAt.ShouldBeNull();
        reply.Stops.ShouldBeEmpty();
        var admission = services.GetRequiredService<RailReadAdmission>();
        admission.TryEnter().ShouldBeTrue();
        admission.TryEnter().ShouldBeFalse();
        admission.Release();
        stream.Release.TrySetResult();
        var settled = false;
        for (var i = 0; i < 100; i++)
        {
            if (admission.TryEnter())
            {
                var second = admission.TryEnter();
                admission.Release();
                if (second)
                {
                    admission.Release();
                    settled = true;
                    break;
                }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }
        settled.ShouldBeTrue();
    }
}
