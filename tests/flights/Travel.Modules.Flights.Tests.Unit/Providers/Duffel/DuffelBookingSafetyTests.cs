using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelBookingSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money Total = Money
        .Create(250m, CurrencyCode.Create("USD").Value)
        .Value;

    private static string Order(
        string amount = "250.00",
        string? currency = "USD",
        string id = "ord_test",
        bool? paid = false,
        string? deadline = "2026-06-01T14:00:00Z"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    id,
                    total_amount = amount,
                    total_currency = currency,
                    payment_status = new
                    {
                        awaiting_payment = paid.HasValue ? !paid.Value : (bool?)null,
                        payment_required_by = deadline,
                    },
                },
            }
        );

    private static string Receipt(
        string status = "succeeded",
        string order = "ord_test",
        string? currency = "USD",
        string amount = "250.00",
        string? id = "pay_test",
        string type = "balance"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    id,
                    status,
                    order_id = order,
                    currency,
                    amount,
                    type,
                },
            }
        );

    [Theory]
    [InlineData(
        "251.00",
        "USD",
        "ord_test",
        false,
        "2026-06-01T14:00:00Z",
        "Flights.OrderPriceChanged"
    )]
    [InlineData(
        "250.00",
        "EUR",
        "ord_test",
        false,
        "2026-06-01T14:00:00Z",
        "Flights.OrderPriceChanged"
    )]
    [InlineData(
        "250.00",
        "USD",
        "ord_other",
        false,
        "2026-06-01T14:00:00Z",
        "Flights.ConfirmationOutcomeUnknown"
    )]
    [InlineData(
        "250.00",
        "USD",
        "ord_test",
        true,
        "2026-06-01T14:00:00Z",
        "Flights.ConfirmationOutcomeUnknown"
    )]
    [InlineData(
        "250.00",
        "USD",
        "ord_test",
        null,
        "2026-06-01T14:00:00Z",
        "Flights.ConfirmationOutcomeUnknown"
    )]
    [InlineData("250.00", "USD", "ord_test", false, null, "Flights.ConfirmationOutcomeUnknown")]
    [InlineData("250.00", "USD", "ord_test", false, "2026-06-01T12:00:00Z", "Flights.HoldExpired")]
    public async Task Final_order_check_refuses_to_pay_unaccepted_or_unpayable_order(
        string amount,
        string currency,
        string id,
        bool? paid,
        string? deadline,
        string code
    )
    {
        var handler = new FakeHttp(Order(amount, currency, id, paid, deadline), Receipt());
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        var preflight = await provider.ValidateConfirmationAsync(
            "ord_test",
            Total,
            TestContext.Current.CancellationToken
        );
        preflight.IsError.ShouldBeTrue();
        preflight.FirstError.Code.ShouldBe(code);
        handler.Posts.ShouldBe(0);
        var result = await provider.ConfirmOrderAsync(
            "ord_test",
            PaymentRef.New(),
            Total,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(code);
        handler.Posts.ShouldBe(0);
    }

    [Theory]
    [InlineData("pending", "ord_test", "USD", "250.00", "pay_test", "balance", 200)]
    [InlineData("failed", "ord_test", "USD", "250.00", "pay_test", "balance", 200)]
    [InlineData("cancelled", "ord_test", "USD", "250.00", "pay_test", "balance", 200)]
    [InlineData("succeeded", "ord_other", "USD", "250.00", "pay_test", "balance", 200)]
    [InlineData("succeeded", "ord_test", null, "250.00", "pay_test", "balance", 200)]
    [InlineData("succeeded", "ord_test", "EUR", "250.00", "pay_test", "balance", 200)]
    [InlineData("succeeded", "ord_test", "USD", "251.00", "pay_test", "balance", 200)]
    [InlineData("succeeded", "ord_test", "USD", "250.00", null, "balance", 200)]
    [InlineData("succeeded", "ord_test", "USD", "250.00", "pay_test", "card", 200)]
    [InlineData("succeeded", "ord_test", "USD", "250.00", "pay_test", "balance", 202)]
    public async Task Unproved_payment_receipt_is_unknown(
        string status,
        string order,
        string? currency,
        string amount,
        string? id,
        string type,
        int responseStatus
    )
    {
        using var http = new HttpClient(
            new FakeHttp(
                Order(),
                Receipt(status, order, currency, amount, id, type),
                responseStatus
            )
        );
        var result = await Provider(http)
            .ConfirmOrderAsync(
                "ord_test",
                PaymentRef.New(),
                Total,
                TestContext.Current.CancellationToken
            );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ConfirmationOutcomeUnknown");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    public async Task Malformed_payment_receipt_is_unknown(string receipt)
    {
        using var http = new HttpClient(new FakeHttp(Order(), receipt));
        var result = await Provider(http)
            .ConfirmOrderAsync(
                "ord_test",
                PaymentRef.New(),
                Total,
                TestContext.Current.CancellationToken
            );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ConfirmationOutcomeUnknown");
    }

    [Fact]
    public async Task Successful_payment_uses_accepted_total_and_current_payments_contract()
    {
        var handler = new FakeHttp(Order("250"), Receipt());
        using var http = new HttpClient(handler);
        var result = await Provider(http)
            .ConfirmOrderAsync(
                "ord_test",
                PaymentRef.New(),
                Total,
                TestContext.Current.CancellationToken
            );
        result.IsError.ShouldBeFalse();
        handler.PostPath.ShouldBe("/air/payments");
        using var body = JsonDocument.Parse(handler.PostBody!);
        var data = body.RootElement.GetProperty("data");
        data.GetProperty("order_id").GetString().ShouldBe("ord_test");
        var payment = data.GetProperty("payment");
        payment.GetProperty("amount").GetString().ShouldBe("250.00");
        payment.GetProperty("currency").GetString().ShouldBe("USD");
        payment.GetProperty("type").GetString().ShouldBe("balance");
        handler.Key.ShouldBeNull();
    }

    [Theory]
    [InlineData(422)]
    [InlineData(500)]
    public async Task Supplier_hold_failure_is_unknown_after_possible_dispatch(int status)
    {
        using var http = new HttpClient(new FakeHttp(Order(), "sensitive@example.test", status));
        var offer = new BookableOffer(
            OfferId.New(),
            null!,
            Total,
            ProviderId.Duffel,
            Now,
            Now.AddHours(1),
            null!,
            "off_test",
            TestPii.Binding().Party
        );
        var passenger = PassengerInfo
            .Create(
                "Test",
                "Person",
                new DateOnly(1990, 1, 1),
                Gender.Male,
                "fictional@example.test",
                PhoneNumber.Create("+79161234567").Value,
                DateOnly.FromDateTime(Now.DateTime)
            )
            .Value;
        var result = await Provider(http)
            .HoldOfferAsync(
                offer with
                {
                    Party = TestPii.Binding().Party,
                },
                TestPii.Binding(),
                TestPii.Passengers(TestPii.Binding(), passenger),
                TestContext.Current.CancellationToken
            );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.HoldOutcomeUnknown");
        result.FirstError.NumericType.ShouldBe(503);
        result.FirstError.Description.ShouldNotContain("sensitive@example.test");
    }

    [Theory]
    [InlineData("GET", false, 4)]
    [InlineData("HEAD", false, 4)]
    [InlineData("POST", false, 1)]
    [InlineData("POST", true, 1)]
    public async Task Actual_Duffel_registration_never_retries_post_even_with_key(
        string method,
        bool key,
        int calls
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:travel"] =
                    "Host=localhost;Database=fictional;Username=x;Password=x",
                ["ConnectionStrings:redis"] = "localhost:6379",
            }
        );
        builder.AddFlightsModule();
        var handler = new FakeHttp("{}", "{}", 500) { GetStatus = 500 };
        builder
            .Services.AddHttpClient<DuffelClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = builder.Services.BuildServiceProvider();
        using var http = services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(typeof(DuffelClient).Name);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            "https://example.invalid/air/orders"
        );
        if (key)
            request.Headers.Add("Idempotency-Key", "fictional");
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        handler.Calls.ShouldBe(calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supplier_transport_failure_is_unknown_without_sensitive_details(bool hold)
    {
        using var http = new HttpClient(
            new ThrowingHttp(new HttpRequestException("supplier-private@example.test"))
        );
        var provider = Provider(http);
        if (hold)
        {
            var result = await provider.HoldOfferAsync(
                TestOffer(),
                TestPii.Binding(),
                TestPii.Passengers(TestPii.Binding(), TestPassenger()),
                TestContext.Current.CancellationToken
            );
            result.FirstError.Code.ShouldBe("Flights.HoldOutcomeUnknown");
            result.FirstError.Description.ShouldNotContain("supplier-private");
        }
        else
        {
            var result = await provider.ConfirmOrderAsync(
                "ord_test",
                PaymentRef.New(),
                Total,
                TestContext.Current.CancellationToken
            );
            result.FirstError.Code.ShouldBe("Flights.ConfirmationOutcomeUnknown");
            result.FirstError.Description.ShouldNotContain("supplier-private");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supplier_cancellation_stays_cancelled_and_has_no_sensitive_chain(
        bool taskCancelled
    )
    {
        using var cts = new CancellationTokenSource();
        OperationCanceledException original = taskCancelled
            ? new TaskCanceledException(
                "supplier-private@example.test",
                new IOException("private-body"),
                cts.Token
            )
            : new OperationCanceledException(
                "supplier-private@example.test",
                new IOException("private-body"),
                cts.Token
            );
        using var http = new HttpClient(new ThrowingHttp(original));
        var pending = Provider(http)
            .HoldOfferAsync(
                TestOffer(),
                TestPii.Binding(),
                TestPii.Passengers(TestPii.Binding(), TestPassenger()),
                cts.Token
            );
        OperationCanceledException? error = null;
        try
        {
            await pending;
        }
        catch (OperationCanceledException caught)
        {
            error = caught;
        }
        error.ShouldNotBeNull();
        error.CancellationToken.ShouldBe(cts.Token);
        pending.IsCanceled.ShouldBeTrue();
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("supplier-private");
        error.ToString().ShouldNotContain("private-body");
    }

    private static BookableOffer TestOffer() =>
        new(
            OfferId.New(),
            null!,
            Total,
            ProviderId.Duffel,
            Now,
            Now.AddHours(1),
            null!,
            "off_test",
            TestPii.Binding().Party
        );

    private static PassengerInfo TestPassenger() =>
        PassengerInfo
            .Create(
                "Test",
                "Person",
                new DateOnly(1990, 1, 1),
                Gender.Male,
                "fictional@example.test",
                PhoneNumber.Create("+79161234567").Value,
                DateOnly.FromDateTime(Now.DateTime)
            )
            .Value;

    private sealed class ThrowingHttp(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) => Task.FromException<HttpResponseMessage>(error);
    }

    [Fact]
    public async Task Price_change_after_preflight_is_rechecked_before_payment_post()
    {
        var handler = new FakeHttp(Order(), Receipt()) { NextOrder = Order("251.00") };
        using var http = new HttpClient(handler);
        var provider = Provider(http);
        var preflight = await provider.ValidateConfirmationAsync(
            "ord_test",
            Total,
            TestContext.Current.CancellationToken
        );
        preflight.IsError.ShouldBeFalse();
        var result = await provider.ConfirmOrderAsync(
            "ord_test",
            PaymentRef.New(),
            Total,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Flights.OrderPriceChanged");
        handler.Calls.ShouldBe(2);
        handler.Posts.ShouldBe(0);
    }

    [Fact]
    public async Task Whole_party_hold_maps_persisted_refs_and_explicit_fields_in_one_post()
    {
        var binding = TestPii.Binding(2);
        var first = BookingPassenger
            .Create(
                binding.Slots[0].Id,
                BookingPassengerDetails
                    .Create(TestPassenger(), PassengerTitle.Create("dr").Value)
                    .Value
            )
            .Value;
        var second = BookingPassenger
            .Create(
                binding.Slots[1].Id,
                BookingPassengerDetails
                    .CreateRaw(
                        binding.Slots[1].Id,
                        "ms",
                        "Anna",
                        "Person",
                        new DateOnly(1992, 2, 2),
                        "female",
                        "anna@example.test",
                        "+79167654321",
                        new DateOnly(2026, 6, 1)
                    )
                    .Value
            )
            .Value;
        var people = new EquatableArray<BookingPassenger>([second, first]);
        var handler = new FakeHttp(Order(), Order());
        using var http = new HttpClient(handler);
        var result = await Provider(http)
            .HoldOfferAsync(
                TestOffer() with
                {
                    Party = binding.Party,
                },
                binding,
                people,
                TestContext.Current.CancellationToken
            );
        result.IsError.ShouldBeFalse();
        handler.Posts.ShouldBe(1);
        using var json = JsonDocument.Parse(handler.PostBody!);
        var passengers = json.RootElement.GetProperty("data").GetProperty("passengers");
        passengers.GetArrayLength().ShouldBe(2);
        for (var i = 0; i < 2; i++)
            passengers[i]
                .GetProperty("id")
                .GetString()
                .ShouldBe(binding.Slots[i].SupplierReference.Value);
        passengers[0].GetProperty("title").GetString().ShouldBe("dr");
        passengers[0].GetProperty("gender").GetString().ShouldBe("m");
        passengers[0].GetProperty("given_name").GetString().ShouldBe("Test");
        passengers[1].GetProperty("title").GetString().ShouldBe("ms");
        passengers[1].GetProperty("gender").GetString().ShouldBe("f");
        passengers[1].GetProperty("given_name").GetString().ShouldBe("Anna");
        passengers[1].GetProperty("email").GetString().ShouldBe("anna@example.test");
        passengers[1].GetProperty("phone_number").GetString().ShouldBe("+79167654321");
        passengers[1].GetProperty("born_on").GetString().ShouldBe("1992-02-02");
        foreach (var passenger in passengers.EnumerateArray())
            passenger.TryGetProperty("type", out _).ShouldBeFalse();
    }

    private static DuffelFlightBookingProvider Provider(HttpClient http)
    {
        http.BaseAddress = new Uri("https://example.invalid");
        return new(
            new DuffelClient(http, Options.Create(new DuffelOptions { ApiKey = "fictional" })),
            new FakeTimeProvider(Now),
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
    }

    private sealed class FakeHttp(string order, string receipt, int status = 200)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int Posts { get; private set; }
        public string? PostPath { get; private set; }
        public string? PostBody { get; private set; }
        public string? Key { get; private set; }
        public int GetStatus { get; init; } = 200;
        public string? NextOrder { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                PostPath = request.RequestUri!.AbsolutePath;
                PostBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(ct);
                Key = request.Headers.TryGetValues("Idempotency-Key", out var keys)
                    ? keys.Single()
                    : null;
            }
            return new HttpResponseMessage(
                (HttpStatusCode)(request.Method == HttpMethod.Post ? status : GetStatus)
            )
            {
                Content = new StringContent(
                    request.Method == HttpMethod.Post ? receipt
                        : Calls > 1 && NextOrder is not null ? NextOrder
                        : order,
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
