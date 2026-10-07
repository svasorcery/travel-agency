using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class DuffelPurchaseTests
{
    [Fact]
    public async Task Matching_service_purchase_pays_once_and_returns_typed_paid_order_proof()
    {
        var clock = AncillaryCatalogTests.Clock;
        var dto = AncillaryCatalogTests.Offer();
        var offer = DuffelOfferMapper.Map(dto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var catalog = DuffelAncillaryMapper.Map(dto, null, false, clock).Value;
        var purchase = BookingPurchaseFactory
            .Select(catalog, binding, Guid.NewGuid(), [new(catalog.Services[0].Key, 2)])
            .Value;
        var attempt = Guid.NewGuid();
        var order = BookedServiceMappingTests.Order(dto, attempt);
        var handler = new PaidOrderHttp(order);
        using var http = new HttpClient(handler) { BaseAddress = new("https://supplier.invalid") };
        var provider = new DuffelFlightBookingProvider(
            new DuffelClient(http, Options.Create(new DuffelOptions { ApiKey = "fictional" })),
            clock,
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
        var result = await provider.ConfirmOrderAsync(
            order.Id,
            PaymentRef.New(),
            purchase.Total,
            new(offer, binding, purchase, attempt),
            _ => Task.FromResult(true),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        handler.Posts.ShouldBe(1);
        result.Value.ServiceProof!.Matches(purchase, order.Id).ShouldBeTrue();
    }

    private sealed class PaidOrderHttp(DuffelOrderDto order) : HttpMessageHandler
    {
        public int Posts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent(
                        new DuffelOrderResponseDto(
                            Posts == 0 ? order : order with { PaymentStatus = new(null, false) }
                        )
                    ),
                };
            request.RequestUri!.AbsolutePath.ShouldBe("/air/payments");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            body.RootElement.GetProperty("data")
                .GetProperty("payment")
                .GetProperty("amount")
                .GetString()
                .ShouldBe("70.00");
            Posts++;
            return new(HttpStatusCode.Created)
            {
                Content = JsonContent(
                    new DuffelPaymentResponseDto(
                        new("pay_fictional", order.Id, "succeeded", "balance", "70.00", "GBP")
                    )
                ),
            };
        }
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("expired-during-fence")]
    public async Task Latest_order_read_must_still_allow_payment_before_supplier_POST(string change)
    {
        var clock = new FakeTimeProvider(AncillaryCatalogTests.Clock.GetUtcNow());
        var dto = AncillaryCatalogTests.Offer();
        var offer = DuffelOfferMapper.Map(dto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var catalog = DuffelAncillaryMapper.Map(dto, null, false, clock).Value;
        var purchase = BookingPurchaseFactory
            .Select(catalog, binding, Guid.NewGuid(), [new(catalog.Services[0].Key, 2)])
            .Value;
        var attempt = Guid.NewGuid();
        var order = BookedServiceMappingTests.Order(dto, attempt);
        var latest = change switch
        {
            "cancelled" => order with { CancelledAt = clock.GetUtcNow() },
            "expired" => order with { PaymentStatus = new(clock.GetUtcNow(), true) },
            _ => order,
        };
        var handler = new LatestOrderHttp(order, latest);
        using var http = new HttpClient(handler) { BaseAddress = new("https://supplier.invalid") };
        var provider = new DuffelFlightBookingProvider(
            new DuffelClient(http, Options.Create(new DuffelOptions { ApiKey = "fictional" })),
            clock,
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
        var result = await provider.ConfirmOrderAsync(
            order.Id,
            PaymentRef.New(),
            purchase.Total,
            new(offer, binding, purchase, attempt),
            _ =>
            {
                if (change == "expired-during-fence")
                    clock.Advance(TimeSpan.FromHours(3));
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        handler.Posts.ShouldBe(0);
    }

    private sealed class LatestOrderHttp(DuffelOrderDto initial, DuffelOrderDto latest)
        : HttpMessageHandler
    {
        private int reads;
        public int Posts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent(
                            new DuffelOrderResponseDto(++reads == 1 ? initial : latest)
                        ),
                    }
                );
            request.RequestUri!.AbsolutePath.ShouldBe("/air/payments");
            Posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        }
    }

    [Fact]
    public async Task Base_offer_refresh_rejects_nonempty_intended_pricing_before_any_create()
    {
        var dto = AncillaryCatalogTests.Offer() with
        {
            IntendedPaymentMethods = JsonSerializer.SerializeToElement(new[] { "card" }),
        };
        using var http = new HttpClient(new OfferHttp(dto))
        {
            BaseAddress = new("https://supplier.invalid"),
        };
        var provider = new DuffelFlightBookingProvider(
            new DuffelClient(http, Options.Create(new DuffelOptions { ApiKey = "fictional" })),
            AncillaryCatalogTests.Clock,
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
        var result = await provider.RefreshOfferAsync(
            dto.Id,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PricingIntentUnsupported");
    }

    private sealed class OfferHttp(DuffelOfferDto offer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            request.Method.ShouldBe(HttpMethod.Get);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent(new DuffelOfferResponseDto(offer)),
                }
            );
        }
    }

    [Fact]
    public async Task Exactly_one_hold_POST_contains_selected_services_and_no_payment()
    {
        var dto = AncillaryCatalogTests.Offer();
        var attempt = Guid.NewGuid();
        var handler = new InlineHttp(
            (body, _) =>
            {
                body.GetProperty("type").GetString().ShouldBe("hold");
                body.EnumerateObject().Any(p => p.Name == "payments").ShouldBeFalse();
                body.GetProperty("services")[0].GetProperty("quantity").GetInt32().ShouldBe(2);
                body.GetProperty("passengers").GetArrayLength().ShouldBe(2);
                body.GetProperty("metadata")
                    .GetProperty("travel_creation")
                    .GetString()
                    .ShouldBe(attempt.ToString("N"));
                return new(HttpStatusCode.Created)
                {
                    Content = JsonContent(
                        new DuffelOrderResponseDto(BookedServiceMappingTests.Order(dto, attempt))
                    ),
                };
            }
        );
        var result = await Execute(handler, attempt);
        result.Outcome.ShouldBe(BookingCreationOutcome.Matches);
        result.Order!.Total.Amount.ShouldBe(70);
        handler.Posts.ShouldBe(1);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("transport")]
    [InlineData("missing-receipt")]
    public async Task Unproven_result_has_no_retry_and_never_claims_no_effects(string mode)
    {
        var dto = AncillaryCatalogTests.Offer();
        var attempt = Guid.NewGuid();
        var handler = new InlineHttp(
            (_, _) =>
            {
                if (mode == "transport")
                    throw new HttpRequestException("fictional-private@example.test");
                var order = BookedServiceMappingTests.Order(dto, attempt) with { Metadata = null };
                return new(HttpStatusCode.Created)
                {
                    Content =
                        mode == "malformed"
                            ? new StringContent("{broken")
                            : JsonContent(new DuffelOrderResponseDto(order)),
                };
            }
        );
        var result = await Execute(handler, attempt);
        result.Outcome.ShouldBe(BookingCreationOutcome.ManualReviewRequired);
        result.PositiveNoEffects.ShouldBeFalse();
        result.Reason.ShouldNotContain("private");
        handler.Posts.ShouldBe(1);
    }

    [Fact]
    public async Task Structured_unsupported_services_rejection_is_no_create_without_fallback()
    {
        var handler = new InlineHttp(
            (_, _) =>
                new(HttpStatusCode.UnprocessableEntity)
                {
                    Content = JsonContent(
                        new
                        {
                            errors = new[] { new { code = "services_not_allowed_for_order_type" } },
                        }
                    ),
                }
        );
        var result = await Execute(handler, Guid.NewGuid());
        result.Outcome.ShouldBe(BookingCreationOutcome.NotCreated);
        result.PositiveNoEffects.ShouldBeTrue();
        handler.Posts.ShouldBe(1);
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<BookingCreationObservation> Execute(InlineHttp handler, Guid attempt)
    {
        var clock = AncillaryCatalogTests.Clock;
        var dto = AncillaryCatalogTests.Offer();
        var offer = DuffelOfferMapper.Map(dto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var facts = DuffelAncillaryMapper.Map(dto, null, false, clock).Value;
        var purchase = BookingPurchaseFactory
            .Select(facts, binding, Guid.NewGuid(), [new(facts.Services[0].Key, 2)])
            .Value;
        var party = new EquatableArray<BookingPassenger>(
            binding
                .Slots.Select(slot =>
                    BookingPassenger
                        .Create(
                            slot.Id,
                            BookingPassengerDetails
                                .CreateRaw(
                                    slot.Id,
                                    "mr",
                                    "Fictional",
                                    "Traveler",
                                    new(1990, 1, 1),
                                    "male",
                                    "demo@example.test",
                                    "+441234567890",
                                    new(2030, 1, 1)
                                )
                                .Value
                        )
                        .Value
                )
                .ToArray()
        );
        using var http = new HttpClient(handler) { BaseAddress = new("https://supplier.invalid") };
        var options = Options.Create(new DuffelOptions { ApiKey = "fictional" });
        var provider = new DuffelFlightBookingProvider(
            new DuffelClient(http, options),
            clock,
            NullLogger<DuffelFlightBookingProvider>.Instance,
            new DuffelOrderCreationClient(http, options)
        );
        var result = await provider.HoldOfferAsync(
            offer,
            binding,
            party,
            purchase,
            attempt,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        return result.Value;
    }

    private sealed class InlineHttp(
        Func<JsonElement, CancellationToken, HttpResponseMessage> respond
    ) : HttpMessageHandler
    {
        public int Posts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            request.Method.ShouldBe(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.ShouldBe("/air/orders");
            Posts++;
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            return respond(document.RootElement.GetProperty("data"), ct);
        }
    }
}
