using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// HTTP-pipeline tests for the Flights endpoints: they exercise the real ASP.NET request
/// pipeline — routing, authentication, the <c>RequireAuthenticatedUser</c> fallback policy,
/// per-endpoint <c>[Authorize]</c> / <c>[AllowAnonymous]</c> intent, the idempotency
/// middleware and model binding — with the message bus stubbed by <see cref="FakeMessageBus"/>.
///
/// These complement the direct-method endpoint tests in the Flights integration project,
/// which invoke the endpoint methods directly and so cannot observe authorization or routing.
/// </summary>
/// <remarks>
/// No <c>[Trait("Category", "Integration")]</c>: the <see cref="FlightsApiFixture"/> is an
/// in-memory <c>TestServer</c> — no Docker, Testcontainers or live PostgreSQL / NATS required.
/// Keeping this trait-free ensures these tests run in the fast, no-Docker CI slot.
/// </remarks>
[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsEndpointsHttpTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture _fixture;

    public FlightsEndpointsHttpTests(FlightsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Bus.Reset();
        _fixture.IdempotencyStore.Reset();
        _fixture.SseRegistry.Reset();
    }

    private const string MalformedUserIdentifier = "traveler@example.test";

    [Fact]
    public async Task Anonymous_quote_exposes_the_fare_facts_used_by_hold()
    {
        var id = Guid.Parse("88b83d41-0194-2098-c1f6-fe7351d41cf2");
        var led = IataCode.Create("LED").Value;
        var dme = IataCode.Create("DME").Value;
        var segment = Segment
            .Create(
                led,
                dme,
                DateTimeOffset.Parse("2030-06-10T10:00:00+03:00"),
                DateTimeOffset.Parse("2030-06-10T12:00:00+03:00"),
                "SU",
                "SU101",
                CabinClass.Economy
            )
            .Value;
        var offer = new BookableOffer(
            new OfferId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            Itinerary.Create([Slice.Create([segment]).Value]).Value,
            Money.Create(10800m, CurrencyCode.Create("RUB").Value).Value,
            ProviderId.Duffel,
            DateTimeOffset.Parse("2030-06-09T23:39:00+00:00"),
            DateTimeOffset.Parse("2030-06-09T23:59:00+00:00"),
            new FareConditions(true, false, "FLEX", "Economy", 1, 1),
            "off_fixture_ow_2030-06-10"
        );
        var binding = FixtureBinding("oneWay");
        offer = offer with { Party = binding.Party };
        _fixture.Bus.OnCapture<QuoteOfferCommand>(command =>
        {
            command.PassengerCount.ShouldBe(1);
            return (ErrorOr<QuotedOfferResult>)new QuotedOfferResult(id, offer, binding);
        });

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/orders/quote",
            new { providerOfferRef = "off_fixture_ow_2030-06-10", provider = "duffel" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        body.RootElement.GetProperty("aggregateId").GetGuid().ShouldBe(id);
        var fare = body.RootElement.GetProperty("fareConditions");
        fare.GetProperty("changeAllowed").GetBoolean().ShouldBeTrue();
        fare.GetProperty("refundAllowed").GetBoolean().ShouldBeFalse();
        fare.GetProperty("fareBasisCode").GetString().ShouldBe("FLEX");
        fare.GetProperty("checkedBaggageQuantity").GetInt32().ShouldBe(1);
        fare.GetProperty("carryOnBaggageQuantity").GetInt32().ShouldBe(1);
        using var canonical = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("tests", "fixtures", "flights-booking.json"))
        );
        JsonElement
            .DeepEquals(
                body.RootElement,
                canonical.RootElement.GetProperty("oneWay").GetProperty("response")
            )
            .ShouldBeTrue("Quote HTTP response drifted from the shared booking fixture");
    }

    [Theory]
    [InlineData("roundTrip", true, false)]
    [InlineData("reQuoteChanged", false, true)]
    [InlineData("groupTwo", false, false)]
    [InlineData("groupNine", false, false)]
    public async Task Quote_round_trip_and_requote_match_shared_wire_examples(
        string caseName,
        bool roundTrip,
        bool priceChanged
    )
    {
        using var canonical = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("tests", "fixtures", "flights-booking.json"))
        );
        var example = canonical.RootElement.GetProperty(caseName);
        var responseExample = example.GetProperty("response");
        var id = responseExample.GetProperty("aggregateId").GetGuid();
        var outbound = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                DateTimeOffset.Parse("2030-06-10T10:00:00+03:00"),
                DateTimeOffset.Parse("2030-06-10T12:00:00+03:00"),
                "SU",
                "SU101",
                CabinClass.Economy
            )
            .Value;
        var slices = new List<Slice> { Slice.Create([outbound]).Value };
        if (roundTrip)
        {
            var inbound = Segment
                .Create(
                    IataCode.Create("DME").Value,
                    IataCode.Create("LED").Value,
                    DateTimeOffset.Parse("2030-06-17T17:00:00+03:00"),
                    DateTimeOffset.Parse("2030-06-17T19:00:00+03:00"),
                    "SU",
                    "SU102",
                    CabinClass.Economy
                )
                .Value;
            slices.Add(Slice.Create([inbound]).Value);
        }
        var rub = CurrencyCode.Create("RUB").Value;
        var amount = responseExample.GetProperty("offer").GetProperty("totalAmount").GetDecimal();
        var offer = new BookableOffer(
            new OfferId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            Itinerary.Create(slices).Value,
            Money.Create(amount, rub).Value,
            ProviderId.Duffel,
            DateTimeOffset.Parse("2030-06-09T23:39:00+00:00"),
            DateTimeOffset.Parse("2030-06-09T23:59:00+00:00"),
            new FareConditions(true, false, "FLEX", "Economy", 1, 1),
            example.GetProperty("request").GetProperty("providerOfferRef").GetString()!
        );
        var binding = FixtureBinding(caseName);
        offer = offer with { Party = binding.Party };
        _fixture.Bus.OnCapture<QuoteOfferCommand>(command =>
        {
            command.PassengerCount.ShouldBe(binding.Party.PassengerCount);
            command.ProviderOfferRef.ShouldBe(
                example.GetProperty("request").GetProperty("providerOfferRef").GetString()
            );
            return (ErrorOr<QuotedOfferResult>)
                new QuotedOfferResult(
                    id,
                    offer,
                    binding,
                    PriceChanged: priceChanged,
                    OldAmount: priceChanged ? Money.Create(10800m, rub).Value : null,
                    NewAmount: priceChanged ? Money.Create(10900m, rub).Value : null
                );
        });

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/orders/quote",
            example.GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        JsonElement
            .DeepEquals(body.RootElement, responseExample)
            .ShouldBeTrue($"Quote HTTP response drifted from {caseName} fixture");
        body.RootElement.GetProperty("binding").GetRawText().ShouldNotContain("pas_http_");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "Travel.slnx"))
        )
            directory = directory.Parent;
        directory.ShouldNotBeNull();
        return Path.Combine([directory.FullName, .. segments]);
    }

    [Fact]
    public async Task Completed_SSE_response_exposes_stream_version_and_releases_registration()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _fixture.SseRegistry.Owner = owner;
        _fixture.SseRegistry.OnRegister = channel =>
        {
            channel.Writer.TryWrite(
                new Travel.Modules.Flights.Application.Notifications.SseEvent(
                    "OrderTicketed",
                    id,
                    JsonSerializer.SerializeToElement(new { status = "Ticketed" }),
                    DateTimeOffset.UtcNow,
                    6
                )
            );
            channel.Writer.TryComplete();
        };
        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/events/flights/orders/{id}"),
            owner
        );
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("event: OrderTicketed");
        body.ShouldContain("\"streamVersion\":6");
        _fixture.SseRegistry.Unregistered.ShouldBe(1);
    }

    [Fact]
    public async Task Confirm_returns_command_result_without_follow_up_EF_query()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _fixture.Bus.On<ConfirmOrderCommand>(
            (ErrorOr<ConfirmedOrderResult>)new ConfirmedOrderResult(id, "Confirmed", "payment-ref")
        );
        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/confirm")
            {
                Content = JsonContent.Create(new { aggregateId = id }),
            },
            owner
        );
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _fixture.Bus.InvocationCount.ShouldBe(1);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        body.GetProperty("status").GetString().ShouldBe("Confirmed");
    }

    private static readonly SearchResult EmptySearchResult = new([], []);
    private static readonly object MinimalSearchBody = new
    {
        origin = "LED",
        destination = "DME",
        departureDate = "2026-07-15",
    };

    private HttpRequestMessage Authenticated(HttpRequestMessage request, Guid userId)
    {
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return request;
    }

    // ── [AllowAnonymous] endpoints are reachable without a token ─────────────────

    [Fact]
    public async Task Search_AnonymousRequest_Returns200()
    {
        _fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)EmptySearchResult);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            new
            {
                origin = "LED",
                destination = "DME",
                departureDate = "2026-07-15",
                returnDate = (string?)null,
                passengerCount = 1,
                cabinClass = "economy",
            },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NlSearch_AnonymousRequest_Returns200()
    {
        _fixture.Bus.On<NlSearchQuery>((ErrorOr<SearchResult>)EmptySearchResult);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query = "из Москвы в Питер завтра" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── [Authorize] endpoints reject anonymous requests with 401 ────────────────

    [Theory]
    [InlineData("GET", "/api/flights/orders")]
    [InlineData("GET", "/api/flights/orders/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/flights/orders/hold")]
    [InlineData("POST", "/api/flights/orders/confirm")]
    [InlineData("POST", "/api/flights/orders/11111111-1111-1111-1111-111111111111/cancel")]
    public async Task AuthorizedEndpoint_WithoutToken_Returns401(string method, string route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
            request.Content = JsonContent.Create(new { aggregateId = Guid.NewGuid() });

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        _fixture.Bus.InvocationCount.ShouldBe(0);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListOrders_with_malformed_authenticated_identity_returns_problem_before_dispatch()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/flights/orders");
        request.Headers.Add(TestAuthHandler.UserIdHeader, MalformedUserIdentifier);

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        await AssertInvalidIdentityProblemAsync(response);
        _fixture.Bus.InvocationCount.ShouldBe(0);
        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
    }

    [Fact]
    public async Task Hold_with_malformed_identity_and_missing_key_returns_401_before_store_or_dispatch()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, MalformedUserIdentifier);
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");

        request.Content = JsonContent.Create(new { aggregateId = Guid.NewGuid() });

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        await AssertInvalidIdentityProblemAsync(response);
        _fixture.Bus.InvocationCount.ShouldBe(0);
        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
    }

    [Fact]
    public async Task OrderEvents_with_malformed_authenticated_identity_returns_problem_before_registry_query()
    {
        var orderId = Guid.NewGuid();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/events/flights/orders/{orderId}"
        );
        request.Headers.Add(TestAuthHandler.UserIdHeader, MalformedUserIdentifier);

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        await AssertInvalidIdentityProblemAsync(response);
        _fixture.SseRegistry.LookupCount.ShouldBe(0);
        _fixture.Bus.InvocationCount.ShouldBe(0);
        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
    }

    // ── currency query param + Accept-Language locale ───────────────────────────

    [Fact]
    public async Task Search_reads_currency_from_query()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=USD",
            MinimalSearchBody,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Currency.Value.ShouldBe("USD");
    }

    [Fact]
    public async Task Search_reads_locale_from_accept_language()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search");
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.ParseAdd("en");
        request.Content = JsonContent.Create(MinimalSearchBody);

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Locale.ShouldBe("en");
    }

    [Fact]
    public async Task Search_defaults_currency_rub_and_locale_ru()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        // No currency query param, no Accept-Language header
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search",
            MinimalSearchBody,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Currency.Value.ShouldBe("RUB");
        captured!.Criteria.Locale.ShouldBe("ru");
    }

    // ── flights:book scope enforcement on booking endpoints ─────────────────────

    [Fact]
    public async Task Hold_without_flights_book_scope_is_forbidden()
    {
        // Authenticated (has a user-id header) but the scope claim does NOT contain
        // "flights:book" — the "flights:book" authorization policy should return 403.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        // Intentionally omit X-Test-Scopes (or use an unrelated scope)
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new { aggregateId = Guid.NewGuid() });

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        _fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Hold_with_flights_book_scope_is_allowed()
    {
        _fixture.Bus.On<Travel.Modules.Flights.Application.Commands.HoldOfferCommand>(
            (ErrorOr<Travel.Modules.Flights.Application.Commands.HeldOrderResult>)
                new Travel.Modules.Flights.Application.Commands.HeldOrderResult(
                    Guid.NewGuid(),
                    "ord_test",
                    DateTimeOffset.UtcNow.AddHours(1)
                )
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        // Minimal valid hold-offer body (single passenger)
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(
            new
            {
                aggregateId = Guid.NewGuid(),
                quoteRevision = Guid.Parse("00000000-0000-0000-0000-000000000111"),
                passengers = new[]
                {
                    new
                    {
                        bookingPassengerId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        title = "mr",
                        givenName = "Ivan",
                        familyName = "Petrov",
                        dateOfBirth = "1990-01-01",
                        gender = "male",
                        email = "ivan@test.com",
                        phone = "+79161234567",
                    },
                },
            }
        );

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _fixture.IdempotencyStore.TryBeginCount.ShouldBe(1);
        _fixture.Bus.InvocationCount.ShouldBe(1);
    }

    // ── [Authorize] endpoints accept an authenticated request ───────────────────

    [Fact]
    public async Task ListOrders_WithToken_Returns200()
    {
        _fixture.Bus.On<ListOrdersQuery>(new OrderListView([], 50, 0));

        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Get, "/api/flights/orders"),
            Guid.NewGuid()
        );
        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("", 50, 0)]
    [InlineData("?limit=21&offset=20", 21, 20)]
    public async Task ListOrders_BindsPagingAndAuthenticatedOwner_WithoutPrivateFields(
        string queryString,
        int expectedLimit,
        int expectedOffset
    )
    {
        var userId = Guid.NewGuid();
        var spoofedOwner = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: userId,
            ProviderOrderId: "ord_private",
            Status: "Held",
            TotalAmount: 5420m,
            Currency: "RUB",
            ItineraryJson: "{}",
            TicketNumbers: [],
            BookedAt: DateTimeOffset.Parse("2030-06-01T10:00:00Z"),
            TicketedAt: null,
            CancelledAt: null,
            RefundedAt: null,
            PassengerCount: 1
        );
        ListOrdersQuery? captured = null;
        _fixture.Bus.OnCapture<ListOrdersQuery>(query =>
        {
            captured = query;
            return new OrderListView([view], expectedLimit, expectedOffset);
        });
        var separator = queryString.Length == 0 ? "?" : "&";
        using var request = Authenticated(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/flights/orders{queryString}{separator}userId={spoofedOwner}"
            ),
            userId
        );

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured.UserId.ShouldBe(userId);
        captured.Limit.ShouldBe(expectedLimit);
        captured.Offset.ShouldBe(expectedOffset);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["items", "limit", "offset"]);
        body.GetProperty("limit").GetInt32().ShouldBe(expectedLimit);
        body.GetProperty("offset").GetInt32().ShouldBe(expectedOffset);
        var item = body.GetProperty("items")[0];
        item.GetProperty("aggregateId").GetGuid().ShouldBe(aggregateId);
        item.GetProperty("status").GetString().ShouldBe("Held");
        item.GetProperty("passengerCount").GetInt32().ShouldBe(1);
        item.GetProperty("totalAmount").GetDecimal().ShouldBe(5420m);
        item.GetProperty("currency").GetString().ShouldBe("RUB");
        item.GetProperty("itinerary").ValueKind.ShouldBe(JsonValueKind.Object);
        item.GetProperty("ticketNumbers").ValueKind.ShouldBe(JsonValueKind.Array);
        item.GetProperty("bookedAt")
            .GetDateTimeOffset()
            .ShouldBe(DateTimeOffset.Parse("2030-06-01T10:00:00Z"));
        item.GetProperty("ticketedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("refundedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe([
                "aggregateId",
                "status",
                "totalAmount",
                "currency",
                "itinerary",
                "ticketNumbers",
                "bookedAt",
                "ticketedAt",
                "cancelledAt",
                "refundedAt",
                "passengerCount",
            ]);
        item.GetRawText().ShouldNotContain("fixture@example.test");
    }

    [Fact]
    public async Task GetOrder_WithToken_ReachesHandler()
    {
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: userId,
            ProviderOrderId: "ord_1",
            Status: "Confirmed",
            TotalAmount: 5420m,
            Currency: "RUB",
            ItineraryJson: "{}",
            TicketNumbers: [],
            BookedAt: DateTimeOffset.UtcNow,
            TicketedAt: null,
            CancelledAt: null,
            RefundedAt: null,
            PassengerCount: 1
        );
        _fixture.Bus.On<GetOrderQuery>((ErrorOr<OrderView>)view);

        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/api/flights/orders/{aggregateId}"),
            userId
        );
        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        body.GetProperty("aggregateId").GetGuid().ShouldBe(aggregateId);
        body.GetProperty("status").GetString().ShouldBe("Confirmed");
        body.GetProperty("totalAmount").GetDecimal().ShouldBe(5420m);
        body.GetProperty("passengerCount").GetInt32().ShouldBe(1);
        body.GetProperty("currency").GetString().ShouldBe("RUB");
        body.GetProperty("itinerary").ValueKind.ShouldBe(JsonValueKind.Object);
        body.GetProperty("ticketNumbers").ValueKind.ShouldBe(JsonValueKind.Array);
        body.GetProperty("bookedAt").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("ticketedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("refundedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        body.TryGetProperty("heldUntil", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_builds_response_from_command_snapshot_without_follow_up_query()
    {
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cancelledAt = DateTimeOffset.UtcNow;
        var itinerary = BuildItinerary();
        var amount = Money.Create(5420m, CurrencyCode.Create("RUB").Value).Value;
        CancelOrderCommand? captured = null;
        _fixture.Bus.OnCapture<CancelOrderCommand>(command =>
        {
            captured = command;
            return (ErrorOr<CancelledOrderResult>)
                new CancelledOrderResult(
                    aggregateId,
                    "Cancelled",
                    new OrderCommandSnapshot(
                        amount,
                        itinerary,
                        [],
                        cancelledAt.AddMinutes(-10),
                        null,
                        cancelledAt,
                        null,
                        PassengerCount: 1
                    )
                );
        });

        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Post, $"/api/flights/orders/{aggregateId}/cancel"),
            userId
        );
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.AggregateId.ShouldBe(aggregateId);
        captured.UserId.ShouldBe(userId);
        _fixture.Bus.InvocationCount.ShouldBe(1);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        body.GetProperty("status").GetString().ShouldBe("Cancelled");
        body.GetProperty("passengerCount").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData("Flights.ProviderCancellationNotSupported")]
    [InlineData("Flights.ProviderOrderMissing")]
    [InlineData("Flights.OrderNotCancellable")]
    public async Task Cancel_rejection_is_owner_scoped_and_not_cached_as_success(string code)
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        _fixture.Bus.OnCapture<CancelOrderCommand>(command =>
        {
            command.UserId.ShouldBe(owner);
            command.AggregateId.ShouldBe(id);
            return (ErrorOr<CancelledOrderResult>)Error.Conflict(code, "Safe fixture rejection");
        });
        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Post, $"/api/flights/orders/{id}/cancel"),
            owner
        );
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        problem.GetProperty("type").GetString().ShouldEndWith(code);
        _fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancel_replays_the_bodyless_owner_request_with_no_store()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _fixture.Bus.OnCapture<CancelOrderCommand>(_ =>
            (ErrorOr<CancelledOrderResult>)
                new CancelledOrderResult(
                    id,
                    "Cancelled",
                    new OrderCommandSnapshot(
                        Money.Create(100, CurrencyCode.Create("RUB").Value).Value,
                        BuildItinerary(),
                        [],
                        now.AddMinutes(-10),
                        null,
                        now,
                        null,
                        PassengerCount: 1
                    )
                )
        );
        string? firstBody = null;
        for (var index = 0; index < 2; index++)
        {
            using var request = Authenticated(
                new HttpRequestMessage(HttpMethod.Post, $"/api/flights/orders/{id}/cancel"),
                owner
            );
            request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
            request.Headers.Add("Idempotency-Key", key);
            using var response = await _fixture.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var body = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken
            );
            if (index == 0)
            {
                firstBody = body;
                _fixture.IdempotencyStore.Replay = new("fixture-response-hash", 200, body);
            }
            else
            {
                body.ShouldBe(firstBody);
                response.Headers.GetValues("Idempotency-Replay").Single().ShouldBe("true");
            }
        }
        _fixture.Bus.InvocationCount.ShouldBe(1);
    }

    private static QuoteBinding FixtureBinding(string caseName)
    {
        using var fixture = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("tests", "fixtures", "flights-booking.json"))
        );
        var wire = fixture
            .RootElement.GetProperty(caseName)
            .GetProperty("response")
            .GetProperty("binding");
        var slots = wire.GetProperty("slots").EnumerateArray().ToArray();
        var references = slots
            .Select((_, index) => SupplierPassengerReference.Create($"pas_http_{index}").Value)
            .ToArray();
        var party = BookableOfferParty
            .Create(
                references.Select(reference => new SupplierPassengerSlot(
                    reference,
                    BookingPassengerKind.Adult
                )),
                DateOnly.ParseExact(
                    wire.GetProperty("firstDepartureLocalDate").GetString()!,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                true,
                false
            )
            .Value;
        return QuoteBinding
            .Create(
                wire.GetProperty("revision").GetGuid(),
                party,
                slots.Select(
                    (slot, index) =>
                        new QuotePassengerSlot(
                            BookingPassengerId
                                .Create(slot.GetProperty("bookingPassengerId").GetGuid())
                                .Value,
                            references[index],
                            BookingPassengerKind.Adult
                        )
                )
            )
            .Value;
    }

    private static Itinerary BuildItinerary()
    {
        var departure = DateTimeOffset.UtcNow.AddDays(1);
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                departure,
                departure.AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create([Slice.Create([segment]).Value]).Value;
    }

    private static async Task AssertInvalidIdentityProblemAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var problem = document.RootElement;

        problem.GetProperty("status").GetInt32().ShouldBe(401);
        problem.GetProperty("title").GetString().ShouldBe("Unauthorized");
        problem
            .GetProperty("type")
            .GetString()
            .ShouldBe("https://travel.local/errors/Identity.UserId.Invalid");
        problem
            .GetProperty("detail")
            .GetString()
            .ShouldBe(
                "Authenticated identity must contain one non-empty, unambiguous user identifier."
            );
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem
            .GetProperty("errors")[0]
            .GetProperty("code")
            .GetString()
            .ShouldBe("Identity.UserId.Invalid");

        body.ShouldNotContain(MalformedUserIdentifier);
        body.ToLowerInvariant().ShouldNotContain("exception");
        body.ToLowerInvariant().ShouldNotContain("secret");
    }
}
