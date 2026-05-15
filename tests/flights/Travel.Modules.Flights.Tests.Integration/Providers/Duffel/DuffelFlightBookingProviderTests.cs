using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Duffel;

[Trait("Category", "Integration")]
public sealed class DuffelFlightBookingProviderTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly DuffelFlightBookingProvider _sut;
    private readonly FakeTimeProvider _time;

    public DuffelFlightBookingProviderTests()
    {
        _server = WireMockServer.Start();
        // Fixed "now" well before the offer/order expires
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

        _sut = new DuffelFlightBookingProvider(
            duffelClient,
            _time,
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
    }

    public void Dispose() => _server.Stop();

    // =========================================================================
    // Shared JSON helpers
    // =========================================================================

    // A full offer JSON object used for both RefreshOffer and the hold/confirm flow
    private const string OfferJson = """
        {
          "id": "off_abc123",
          "total_amount": "250.00",
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
        }
        """;

    private const string OrderJson = """
        {
          "id": "ord_xyz789",
          "booking_reference": "ABCDEF",
          "total_amount": "250.00",
          "total_currency": "USD",
          "payment_status": {
            "payment_required_by": "2026-06-01T14:00:00Z"
          },
          "documents": [
            { "type": "ticket", "unique_identifier": "180-1234567890" },
            { "type": "ticket", "unique_identifier": "180-0987654321" }
          ],
          "cancelled_at": null
        }
        """;

    private const string CancelledOrderJson = """
        {
          "id": "ord_xyz789",
          "booking_reference": "ABCDEF",
          "total_amount": "250.00",
          "total_currency": "USD",
          "payment_status": null,
          "documents": [],
          "cancelled_at": "2026-06-02T10:00:00Z"
        }
        """;

    // A minimal BookableOffer used by HoldOfferAsync / ConfirmOrderAsync tests
    private BookableOffer BuildBookableOffer() =>
        new(
            OfferId.New(),
            Itinerary
                .Create(
                    new[]
                    {
                        Slice
                            .Create(
                                new[]
                                {
                                    Segment
                                        .Create(
                                            IataCode.Create("LHR").Value,
                                            IataCode.Create("JFK").Value,
                                            new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero),
                                            new DateTimeOffset(2026, 8, 1, 16, 0, 0, TimeSpan.Zero),
                                            "BA",
                                            "117",
                                            CabinClass.Economy
                                        )
                                        .Value,
                                }
                            )
                            .Value,
                    }
                )
                .Value,
            Money.Create(250m, CurrencyCode.Create("USD").Value).Value,
            ProviderId.Duffel,
            _time.GetUtcNow(),
            new DateTimeOffset(2026, 8, 1, 23, 59, 0, TimeSpan.Zero),
            new FareConditions(true, false, "Economy Flex", "Economy"),
            "off_abc123"
        );

    private PassengerInfo BuildPassenger() =>
        PassengerInfo
            .Create(
                "John",
                "Doe",
                new DateOnly(1985, 5, 15),
                Gender.Male,
                "john.doe@example.com",
                PhoneNumber.Create("+12025550123").Value,
                new DateOnly(2026, 5, 14)
            )
            .Value;

    // =========================================================================
    // RefreshOfferAsync
    // =========================================================================

    [Fact]
    public async Task RefreshOfferAsync_HappyPath_ReturnsBookableOffer()
    {
        _server
            .Given(Request.Create().WithPath("/air/offers/off_abc123").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{OfferJson}}}""")
            );

        var result = await _sut.RefreshOfferAsync("off_abc123", CancellationToken.None);

        result.IsError.ShouldBeFalse();
        var offer = result.Value.ShouldBeOfType<BookableOffer>();
        offer.ProviderOfferRef.ShouldBe("off_abc123");
        offer.TotalAmount.Amount.ShouldBe(250m);
    }

    [Fact]
    public async Task RefreshOfferAsync_404_ReturnsOfferNotFound()
    {
        _server
            .Given(Request.Create().WithPath("/air/offers/off_missing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await _sut.RefreshOfferAsync("off_missing", CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.OfferNotFound("off_missing").Code);
    }

    [Fact]
    public async Task RefreshOfferAsync_AlreadyExpiredOffer_ReturnsOfferExpired()
    {
        // Offer that expired in the past relative to _time (2026-06-01 12:00)
        const string expiredOfferJson = """
            {
              "data": {
                "id": "off_expired",
                "total_amount": "100.00",
                "total_currency": "USD",
                "expires_at": "2026-05-01T00:00:00Z",
                "slices": [
                  {
                    "fare_brand_name": null,
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
                "conditions": null
              }
            }
            """;

        _server
            .Given(Request.Create().WithPath("/air/offers/off_expired").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(expiredOfferJson)
            );

        var result = await _sut.RefreshOfferAsync("off_expired", CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.OfferExpired.Code);
    }

    // =========================================================================
    // HoldOfferAsync
    // =========================================================================

    [Fact]
    public async Task HoldOfferAsync_HappyPath_ReturnsHeldOrderWithProviderOrderId()
    {
        _server
            .Given(Request.Create().WithPath("/air/orders").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{OrderJson}}}""")
            );

        var result = await _sut.HoldOfferAsync(
            BuildBookableOffer(),
            BuildPassenger(),
            CancellationToken.None
        );

        result.IsError.ShouldBeFalse();
        var held = result.Value.ShouldBeOfType<HeldOrder>();
        held.ProviderOrderId.ShouldBe("ord_xyz789");
        held.HeldUntil.ShouldBe(new DateTimeOffset(2026, 6, 1, 14, 0, 0, TimeSpan.Zero));
    }

    // =========================================================================
    // ConfirmOrderAsync
    // =========================================================================

    [Fact]
    public async Task ConfirmOrderAsync_HappyPath_ReturnsConfirmedOrder()
    {
        // GET order
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_xyz789").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{OrderJson}}}""")
            );

        // POST payment
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_xyz789/payments").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data": {"id": "pay_001", "type": "balance"}}""")
            );

        var result = await _sut.ConfirmOrderAsync(
            "ord_xyz789",
            PaymentRef.New(),
            "test-idempotency-key",
            CancellationToken.None
        );

        result.IsError.ShouldBeFalse();
        var confirmed = result.Value.ShouldBeOfType<ConfirmedOrder>();
        confirmed.ProviderOrderId.ShouldBe("ord_xyz789");
        confirmed.ConfirmedAt.ShouldBe(_time.GetUtcNow());
    }

    // =========================================================================
    // CancelOrderAsync
    // =========================================================================

    [Fact]
    public async Task CancelOrderAsync_HappyPath_ReturnsSuccess()
    {
        _server
            .Given(Request.Create().WithPath("/air/order_cancellations").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data": {"id": "canc_001", "order_id": "ord_xyz789"}}""")
            );

        var result = await _sut.CancelOrderAsync("ord_xyz789", CancellationToken.None);

        result.IsError.ShouldBeFalse();
    }

    // =========================================================================
    // GetOrderStatusAsync
    // =========================================================================

    [Fact]
    public async Task GetOrderStatusAsync_HappyPath_ReturnsOrderStatusWithTicketNumbers()
    {
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_xyz789").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{OrderJson}}}""")
            );

        var result = await _sut.GetOrderStatusAsync("ord_xyz789", CancellationToken.None);

        result.IsError.ShouldBeFalse();
        var status = result.Value.ShouldBeOfType<OrderStatus>();
        status.ProviderOrderId.ShouldBe("ord_xyz789");
        // OrderJson has ticket documents and no cancelled_at → Ticketed
        status.Status.ShouldBe(OrderStatusKind.Ticketed);
        status.TicketNumbers.Count.ShouldBe(2);
        status.TicketNumbers.ShouldContain("180-1234567890");
        status.TicketNumbers.ShouldContain("180-0987654321");
    }

    // =========================================================================
    // Task 4.4 — HoldOfferAsync error mapping + hold-expiry fix
    // =========================================================================

    [Fact]
    public async Task Hold_422_maps_to_offer_expired()
    {
        _server
            .Given(Request.Create().WithPath("/air/orders").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(422)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """{"errors":[{"type":"validation_error","code":"offer_no_longer_available"}]}"""
                    )
            );

        var result = await _sut.HoldOfferAsync(
            BuildBookableOffer(),
            BuildPassenger(),
            CancellationToken.None
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.OfferExpired.Code);
    }

    [Fact]
    public async Task Hold_without_payment_required_by_falls_back_to_offer_expires_at()
    {
        // Order response with no payment_required_by
        const string orderNoPaymentRequiredBy = """
            {
              "id": "ord_nopay",
              "booking_reference": "XXXXXX",
              "total_amount": "250.00",
              "total_currency": "USD",
              "payment_status": null,
              "documents": [],
              "cancelled_at": null
            }
            """;

        _server
            .Given(Request.Create().WithPath("/air/orders").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{orderNoPaymentRequiredBy}}}""")
            );

        var offer = BuildBookableOffer();
        var result = await _sut.HoldOfferAsync(offer, BuildPassenger(), CancellationToken.None);

        result.IsError.ShouldBeFalse();
        // Must use the offer's ExpiresAt, not a fabricated +20min from now
        result.Value.HeldUntil.ShouldBe(offer.ExpiresAt);
    }

    // =========================================================================
    // Task 4.6 — GetOrderStatusAsync typed enum + ticketed
    // =========================================================================

    [Fact]
    public async Task GetOrderStatus_reports_ticketed_when_documents_present()
    {
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_ticketed").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        {"data": {
                          "id": "ord_ticketed",
                          "booking_reference": "TICK01",
                          "total_amount": "250.00",
                          "total_currency": "USD",
                          "payment_status": null,
                          "documents": [
                            { "type": "ticket", "unique_identifier": "180-1111111111" }
                          ],
                          "cancelled_at": null
                        }}
                        """
                    )
            );

        var result = await _sut.GetOrderStatusAsync("ord_ticketed", CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(OrderStatusKind.Ticketed);
        result.Value.TicketNumbers.ShouldContain("180-1111111111");
    }

    [Fact]
    public async Task GetOrderStatus_reports_cancelled_when_cancelled_at_set()
    {
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_cancelled").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        $$"""{"data": {{CancelledOrderJson.Replace("ord_xyz789", "ord_cancelled")}}}"""
                    )
            );

        var result = await _sut.GetOrderStatusAsync("ord_cancelled", CancellationToken.None);

        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(OrderStatusKind.Cancelled);
    }

    // =========================================================================
    // Task 4.5 — No raw provider error strings in domain errors
    // =========================================================================

    [Fact]
    public async Task Confirm_failure_does_not_leak_raw_provider_body()
    {
        const string sensitiveBody =
            """{"errors":[{"code":"card_declined","detail":"Card 4111 declined: CVV mismatch, last 4: 1234"}]}""";

        // GET order succeeds
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_leak_test").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        $$"""{"data": {{OrderJson.Replace("ord_xyz789", "ord_leak_test")}}}"""
                    )
            );

        // POST payment returns 422 with sensitive body
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_leak_test/payments").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(422)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(sensitiveBody)
            );

        var result = await _sut.ConfirmOrderAsync(
            "ord_leak_test",
            PaymentRef.New(),
            "key-leak-test",
            CancellationToken.None
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PaymentFailed");
        // The raw provider body must NOT appear in the domain error description
        result.FirstError.Description.ShouldNotContain("card_declined");
        result.FirstError.Description.ShouldNotContain("4111");
        result.FirstError.Description.ShouldNotContain("CVV");
        // Error message should only reference the status code
        result.FirstError.Description.ShouldContain("422");
    }

    [Fact]
    public async Task Cancel_failure_does_not_leak_raw_provider_body()
    {
        const string sensitiveBody =
            """{"errors":[{"code":"internal_error","detail":"DB row 7f3a9b leaked"}]}""";

        _server
            .Given(Request.Create().WithPath("/air/order_cancellations").UsingPost())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(500)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(sensitiveBody)
            );

        var result = await _sut.CancelOrderAsync("ord_cancel_leak", CancellationToken.None);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OrderNotCancellable");
        result.FirstError.Description.ShouldNotContain("internal_error");
        result.FirstError.Description.ShouldNotContain("DB row");
        result.FirstError.Description.ShouldContain("500");
    }

    // =========================================================================
    // Task 4.2 — Idempotency-Key header on ConfirmOrderAsync
    // =========================================================================

    [Fact]
    public async Task Confirm_sends_idempotency_key_to_duffel()
    {
        const string idempotencyKey = "abc123idemkey";

        // GET order
        _server
            .Given(Request.Create().WithPath("/air/orders/ord_xyz789").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody($$"""{"data": {{OrderJson}}}""")
            );

        // POST payment — require Idempotency-Key header to match
        _server
            .Given(
                Request
                    .Create()
                    .WithPath("/air/orders/ord_xyz789/payments")
                    .UsingPost()
                    .WithHeader("Idempotency-Key", idempotencyKey)
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data": {"id": "pay_001", "type": "balance"}}""")
            );

        var result = await _sut.ConfirmOrderAsync(
            "ord_xyz789",
            PaymentRef.New(),
            idempotencyKey,
            CancellationToken.None
        );

        result.IsError.ShouldBeFalse();
        result.Value.ProviderOrderId.ShouldBe("ord_xyz789");

        // Verify the Idempotency-Key was sent to the payments endpoint
        var paymentLogEntry = _server.LogEntries.FirstOrDefault(le =>
            le.RequestMessage.Path == "/air/orders/ord_xyz789/payments"
            && le.RequestMessage.Method == "POST"
        );
        paymentLogEntry.ShouldNotBeNull("No request found to the payments endpoint");
        paymentLogEntry.RequestMessage.Headers!.ShouldContainKey("Idempotency-Key");
        string.Join("", paymentLogEntry.RequestMessage.Headers!["Idempotency-Key"])
            .ShouldBe(idempotencyKey);
    }
}
