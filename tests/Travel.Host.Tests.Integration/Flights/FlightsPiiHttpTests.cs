using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsPiiHttpTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    [Fact]
    public async Task Hold_dispatch_does_not_carry_passenger_plaintext()
    {
        var serialized = "";
        fixture.Bus.Reset();
        fixture.Bus.OnCapture<HoldOfferCommand>(command =>
        {
            serialized = JsonSerializer.Serialize(command);
            return (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(
                    command.AggregateId,
                    "ord_fictional",
                    DateTimeOffset.UtcNow.AddMinutes(10)
                );
        });
        using var request = Request();
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        serialized.ShouldNotBeEmpty();
        serialized.ShouldNotContain("FictionalGiven");
        serialized.ShouldNotContain("distinctive-pii@example.test");
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain("FictionalGiven");
    }

    [Fact]
    public async Task Unavailable_protection_returns_safe_503_before_bus_dispatch()
    {
        fixture.Bus.Reset();
        var dispatched = false;
        fixture.Bus.OnCapture<HoldOfferCommand>(command =>
        {
            dispatched = true;
            return (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(command.AggregateId, "ord_fictional", DateTimeOffset.UtcNow);
        });
        fixture.PassengerPartyProtector.Available = false;
        try
        {
            using var request = Request();
            using var response = await fixture.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            dispatched.ShouldBeFalse();
            var body = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken
            );
            body.ShouldContain("Flights.PiiProtectionUnavailable");
            body.ShouldNotContain("FictionalGiven");
        }
        finally
        {
            fixture.PassengerPartyProtector.Available = true;
        }
    }

    [Fact]
    public async Task Invalid_passenger_field_is_not_echoed_in_problem_details()
    {
        using var request = Request("private-input@example.test");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Flights.PassengerGenderInvalid");
        body.ShouldNotContain("private-input@example.test");
    }

    internal static HttpRequestMessage Request(string genderInput = "female")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold")
        {
            Content = JsonContent.Create(
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
                            givenName = "FictionalGiven",
                            familyName = "FictionalFamily",
                            dateOfBirth = "1987-02-14",
                            gender = genderInput,
                            email = "distinctive-pii@example.test",
                            phone = "+12025550123",
                        },
                    },
                }
            ),
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }
}
