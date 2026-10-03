using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// Real routing/auth/middleware/model binding in the existing lean TestServer fixture.
// No Program/Host runtime, database, schema, Wolverine runtime or supplier is started.
[Collection(HostIntegrationCollection.Name)]
public sealed class BookingRequestBodyLimitHttpTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    private const int Limit = 16 * 1024;

    [Theory]
    [InlineData("/api/flights/orders/hold", true)]
    [InlineData("/api/flights/orders/hold", false)]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/", true)]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/", false)]
    [InlineData("/api/flights/orders/confirm/", true)]
    [InlineData("/api/flights/orders/confirm/", false)]
    [InlineData("/api/flights/orders/11111111-1111-1111-1111-111111111111/cancel/", true)]
    [InlineData("/api/flights/orders/11111111-1111-1111-1111-111111111111/cancel/", false)]
    public async Task Exact_cap_plus_one_returns_413_before_store_model_binding_or_bus(
        string path,
        bool declared
    )
    {
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        // Malformed UTF-8 JSON would produce400 if model binding ran first.
        var bytes = Encoding.UTF8.GetBytes(new string('Ж', Limit / 2) + "x");
        bytes.Length.ShouldBe(Limit + 1);
        using var request = Request(path, bytes, declared);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        fixture.Bus.InvocationCount.ShouldBe(0);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Flights.RequestTooLarge");
        body.ShouldNotContain("Ж");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_valid_hold_never_enters_passenger_processing_or_dispatch(
        bool declared
    )
    {
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        var bytes = ValidHold(Limit + 1);
        // A reached endpoint would fail503 here; the body guard must return413 first.
        fixture.PassengerProtector.Available = false;
        try
        {
            using var request = Request("/api/flights/orders/hold", bytes, declared);
            using var response = await fixture.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
            fixture.Bus.InvocationCount.ShouldBe(0);
        }
        finally
        {
            fixture.PassengerProtector.Available = true;
        }
    }

    [Theory]
    [InlineData("/api/flights/orders/hold", true)]
    [InlineData("/api/flights/orders/hold", false)]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/", true)]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/", false)]
    public async Task Exact_cap_valid_json_reaches_hold_model_binding_and_dispatch(
        string path,
        bool declared
    )
    {
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        fixture.PassengerProtector.Available = true;
        var dispatched = false;
        fixture.Bus.OnCapture<HoldOfferCommand>(command =>
        {
            dispatched = true;
            command.AggregateId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
            return (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(
                    command.AggregateId,
                    "ord_fictional",
                    DateTimeOffset.UtcNow.AddMinutes(10)
                );
        });
        using var request = Request(path, ValidHold(Limit), declared);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(1);
        fixture.Bus.InvocationCount.ShouldBe(1);
        dispatched.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, true, HttpStatusCode.Unauthorized)]
    [InlineData(false, false, HttpStatusCode.Unauthorized)]
    [InlineData(true, true, HttpStatusCode.BadRequest)]
    [InlineData(true, false, HttpStatusCode.BadRequest)]
    public async Task Missing_auth_or_key_short_circuits_before_store_and_dispatch(
        bool authenticated,
        bool declared,
        HttpStatusCode status
    )
    {
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        using var request = Request("/API/FLIGHTS/ORDERS/HOLD/", ValidHold(Limit + 1), declared);
        if (authenticated)
            request.Headers.Remove("Idempotency-Key");
        else
            request.Headers.Remove(TestAuthHandler.UserIdHeader);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(status);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    private static byte[] ValidHold(int length)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                aggregateId = "22222222-2222-2222-2222-222222222222",
                passengers = new[]
                {
                    new
                    {
                        givenName = "Fictional",
                        familyName = "Person",
                        dateOfBirth = "1990-01-01",
                        gender = "female",
                        email = "fictional@example.test",
                        phone = "+12025550123",
                    },
                },
            }
        );
        // JSON whitespace is legal and preserves the independently counted raw boundary.
        var prefix = Encoding.UTF8.GetBytes(json);
        return [.. prefix, .. Encoding.UTF8.GetBytes(new string(' ', length - prefix.Length))];
    }

    private static HttpRequestMessage Request(string path, byte[] bytes, bool declared)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = declared ? new ByteArrayContent(bytes) : new UnknownLengthContent(bytes),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (declared)
            request.Content.Headers.ContentLength = bytes.Length;
        else
            request.Headers.TransferEncodingChunked = true;
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        ) => await stream.WriteAsync(bytes);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
