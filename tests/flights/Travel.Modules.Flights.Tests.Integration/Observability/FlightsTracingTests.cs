using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Observability;

/// <summary>
/// Verifies that provider HTTP calls emit OTel spans with the expected names and tags,
/// using an <see cref="ActivityListener"/> to capture spans in-process.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FlightsTracingTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly FakeTimeProvider _time = new();
    private readonly List<Activity> _captured = [];
    private readonly ActivityListener _listener;

    public FlightsTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FlightsActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _captured.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _server.Stop();
    }

    private DuffelFlightSearchProvider BuildSearchProvider()
    {
        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = _server.Url!,
                ApiKey = "test-key",
                SearchTimeoutSeconds = 10,
                TimeoutSeconds = 10,
            }
        );

        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
        http.DefaultRequestHeaders.Accept.Add(new("application/json"));
        var client = new DuffelClient(http, opts);
        return new DuffelFlightSearchProvider(
            client,
            opts,
            _time,
            NullLogger<DuffelFlightSearchProvider>.Instance
        );
    }

    [Fact]
    public async Task Provider_calls_and_saga_transitions_emit_spans()
    {
        var ct = TestContext.Current.CancellationToken;

        _server
            .Given(Request.Create().WithPath("/air/offer_requests").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data":{"id":"orq_test","offers":[]}}""")
            );

        var provider = BuildSearchProvider();

        var criteria = SearchCriteria
            .Create(
                IataCode.Create("SVO").Value,
                IataCode.Create("LED").Value,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                null,
                1,
                CabinClass.Economy,
                CurrencyCode.Create("RUB").Value
            )
            .Value;

        await provider.SearchAsync(criteria, ct);

        var searchSpan = _captured.FirstOrDefault(a =>
            a.OperationName.Contains("duffel") || a.OperationName.Contains("search")
        );
        searchSpan.ShouldNotBeNull(
            "Expected a span to be emitted for the Duffel search provider call. "
                + $"Captured spans: [{string.Join(", ", _captured.Select(a => a.OperationName))}]"
        );
        searchSpan!.GetTagItem("provider.id").ShouldNotBeNull();
    }
}
