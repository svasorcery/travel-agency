using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Duffel;

[Trait("Category", "Integration")]
public sealed class DuffelFlightSearchProviderTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly DuffelFlightSearchProvider _sut;
    private readonly FakeTimeProvider _time;

    public DuffelFlightSearchProviderTests()
    {
        _server = WireMockServer.Start();
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v2",
                ApiKey = "test_key",
                SearchTimeoutSeconds = 10, // generous timeout so happy-path tests don't race
            }
        );
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var duffelClient = new DuffelClient(http, opts);

        _sut = new DuffelFlightSearchProvider(
            duffelClient,
            opts,
            _time,
            NullLogger<DuffelFlightSearchProvider>.Instance
        );
    }

    public void Dispose() => _server.Stop();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SearchCriteria BuildCriteria() =>
        SearchCriteria
            .Create(
                IataCode.Create("LHR").Value,
                IataCode.Create("JFK").Value,
                new DateOnly(2026, 8, 1),
                returnDate: null,
                passengerCount: 1,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;

    // A minimal 2-offer Duffel offer_request response with all required fields.
    private const string TwoOfferResponse = """
        {
          "data": {
            "offers": [
              {
                "id": "off_001",
                "total_amount": "199.99",
                "total_currency": "USD",
                "expires_at": "2026-08-01T23:59:00Z",
                "slices": [
                  {
                    "fare_brand_name": "Economy Flex",
                    "segments": [
                      {
                        "departing_at": "2026-08-01T08:00:00Z",
                        "arriving_at": "2026-08-01T16:00:00Z",
                        "origin": { "iata_code": "LHR" },
                        "destination": { "iata_code": "JFK" },
                        "marketing_carrier": { "iata_code": "BA" },
                        "marketing_carrier_flight_number": "117",
                        "passengers": [
                          { "cabin_class": "economy", "cabin_class_marketing_name": "Economy" }
                        ]
                      }
                    ]
                  }
                ],
                "conditions": {
                  "change_before_departure": { "allowed": true },
                  "refund_before_departure": { "allowed": false }
                }
              },
              {
                "id": "off_002",
                "total_amount": "349.00",
                "total_currency": "USD",
                "expires_at": "2026-08-01T23:59:00Z",
                "slices": [
                  {
                    "fare_brand_name": null,
                    "segments": [
                      {
                        "departing_at": "2026-08-01T14:00:00Z",
                        "arriving_at": "2026-08-01T22:00:00Z",
                        "origin": { "iata_code": "LHR" },
                        "destination": { "iata_code": "JFK" },
                        "marketing_carrier": { "iata_code": "VS" },
                        "marketing_carrier_flight_number": "3",
                        "passengers": [
                          { "cabin_class": "economy", "cabin_class_marketing_name": "Economy" }
                        ]
                      }
                    ]
                  }
                ],
                "conditions": null
              }
            ]
          }
        }
        """;

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_TwoOfferResponse_ReturnsTwoMappedOffers()
    {
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/air/offer_requests")
                    .WithParam("return_offers", "true")
                    .UsingPost()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(TwoOfferResponse)
            );

        var result = await _sut.SearchAsync(BuildCriteria(), CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull();
        result.Value.Count.ShouldBe(2);

        var first = result.Value[0].ShouldBeOfType<BookableOffer>();
        first.ProviderOfferRef.ShouldBe("off_001");
        first.TotalAmount.Amount.ShouldBe(199.99m);

        var second = result.Value[1].ShouldBeOfType<BookableOffer>();
        second.ProviderOfferRef.ShouldBe("off_002");
        second.TotalAmount.Amount.ShouldBe(349.00m);
    }

    [Fact]
    public async Task SearchAsync_429Response_ReturnsProviderRateLimited()
    {
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/air/offer_requests")
                    .WithParam("return_offers", "true")
                    .UsingPost()
            )
            .RespondWith(Response.Create().WithStatusCode(429));

        var result = await _sut.SearchAsync(BuildCriteria(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderRateLimited("Duffel").Code);
    }

    [Fact]
    public async Task SearchAsync_500Response_ReturnsProviderUnavailable()
    {
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/air/offer_requests")
                    .WithParam("return_offers", "true")
                    .UsingPost()
            )
            .RespondWith(Response.Create().WithStatusCode(500));

        var result = await _sut.SearchAsync(BuildCriteria(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderUnavailable("Duffel").Code);
    }

    // ─── Task 4.8 — failure-path tests ──────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_TaskCanceledException_ReturnsProviderUnavailable()
    {
        // DuffelClient built with a handler that always throws TaskCanceledException
        var throwingHandler = new ThrowingHandler(new TaskCanceledException("simulated timeout"));
        using var http = new HttpClient(throwingHandler)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };
        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = "http://localhost:9999",
                ApiVersion = "v2",
                ApiKey = "test_key",
                SearchTimeoutSeconds = 10,
            }
        );
        var client = new DuffelClient(http, opts);
        var sut = new DuffelFlightSearchProvider(
            client,
            opts,
            _time,
            NullLogger<DuffelFlightSearchProvider>.Instance
        );

        var result = await sut.SearchAsync(BuildCriteria(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderUnavailable("Duffel").Code);
    }

    [Fact]
    public async Task SearchAsync_HttpRequestException_ReturnsProviderUnavailable()
    {
        var throwingHandler = new ThrowingHandler(
            new HttpRequestException("simulated network failure")
        );
        using var http = new HttpClient(throwingHandler)
        {
            BaseAddress = new Uri("http://localhost:9999"),
        };
        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = "http://localhost:9999",
                ApiVersion = "v2",
                ApiKey = "test_key",
                SearchTimeoutSeconds = 10,
            }
        );
        var client = new DuffelClient(http, opts);
        var sut = new DuffelFlightSearchProvider(
            client,
            opts,
            _time,
            NullLogger<DuffelFlightSearchProvider>.Instance
        );

        var result = await sut.SearchAsync(BuildCriteria(), CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderUnavailable("Duffel").Code);
    }

    // ─── Task 4.8 (Fix 2) — per-call 4 s search timeout ────────────────────────

    /// <summary>
    /// SearchAsync must respect the per-call DuffelOptions.SearchTimeoutSeconds budget (4 s)
    /// rather than the client-wide 10 s order timeout. This prevents slow Duffel search
    /// responses from blocking the aggregated search result for the full 10 s window.
    /// </summary>
    [Fact]
    public async Task Search_times_out_at_4s_not_10s()
    {
        // Arrange: endpoint that hangs for 8 s — well beyond the 4 s search budget
        // but below the 10 s client-wide timeout, so the test will only pass if the
        // per-call linked CTS fires at ~4 s.
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/air/offer_requests")
                    .WithParam("return_offers", "true")
                    .UsingPost()
            )
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(8)));

        var searchTimeoutSeconds = 4;
        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v2",
                ApiKey = "test_key",
                TimeoutSeconds = 30, // client-wide budget — deliberately generous so only the search CTS fires
                SearchTimeoutSeconds = searchTimeoutSeconds,
            }
        );
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var duffelClient = new DuffelClient(http, opts);
        var sut = new DuffelFlightSearchProvider(
            duffelClient,
            opts,
            _time,
            NullLogger<DuffelFlightSearchProvider>.Instance
        );

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await sut.SearchAsync(BuildCriteria(), CancellationToken.None);
        sw.Stop();

        // Assert: returns ProviderUnavailable (via TaskCanceledException branch) within the
        // search timeout window. Allow 2 s of slack for CI variance; must NOT approach 10 s.
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderUnavailable("Duffel").Code);
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(
            6,
            $"SearchAsync must timeout at ~{searchTimeoutSeconds}s (search budget), "
                + $"not at 10s (client-wide timeout). Elapsed: {sw.Elapsed.TotalSeconds:F1}s"
        );
    }

    /// <summary>Helper delegating handler that always throws the provided exception.</summary>
    private sealed class ThrowingHandler(Exception ex) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromException<HttpResponseMessage>(ex);
    }
}
