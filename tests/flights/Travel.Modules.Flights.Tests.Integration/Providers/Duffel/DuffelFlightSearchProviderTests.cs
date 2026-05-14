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
            }
        );
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var duffelClient = new DuffelClient(http, opts);

        _sut = new DuffelFlightSearchProvider(
            duffelClient,
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
}
