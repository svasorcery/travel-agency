using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Shouldly;
using Travel.Host.Tests.Integration.Flights;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Documentation;

[Collection(HostIntegrationCollection.Name)]
public sealed class ReadmeRequestExamplesTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture _fixture;
    private static readonly SearchResult EmptySearch = new([], []);

    public ReadmeRequestExamplesTests(FlightsApiFixture fixture)
    {
        _fixture = fixture;
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        fixture.SseRegistry.Reset();
    }

    [Fact]
    public void Catalog_bodies_use_current_DTO_fields_and_explicit_one_way_return_date()
    {
        foreach (var id in new[] { "search", "nlSearch", "quote", "hold", "confirm" })
            ReadmeExamples.ValidateBody(id, ReadmeExamples.ResolvedBody(id)).ShouldNotBeNull();

        using var search = JsonDocument.Parse(ReadmeExamples.ResolvedBody("search"));
        search.RootElement.GetProperty("returnDate").ValueKind.ShouldBe(JsonValueKind.Null);
        search.RootElement.GetProperty("passengerCount").GetInt32().ShouldBe(1);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("nlSearch")]
    [InlineData("quote")]
    [InlineData("hold")]
    [InlineData("confirm")]
    public async Task Catalog_POST_body_reaches_its_expected_typed_command(string id)
    {
        object? captured = null;
        var aggregateId = ReadmeExamples.AggregateId;
        switch (id)
        {
            case "search":
                _fixture.Bus.OnCapture<SearchFlightsQuery>(query =>
                {
                    captured = query;
                    return (ErrorOr<SearchResult>)EmptySearch;
                });
                break;
            case "nlSearch":
                _fixture.Bus.OnCapture<NlSearchQuery>(query =>
                {
                    captured = query;
                    return (ErrorOr<SearchResult>)EmptySearch;
                });
                break;
            case "quote":
                _fixture.Bus.OnCapture<QuoteOfferCommand>(command =>
                {
                    captured = command;
                    return (ErrorOr<QuotedOfferResult>)Error.NotFound("Demo.NoOffer", "Fake offer");
                });
                break;
            case "hold":
                _fixture.Bus.OnCapture<HoldOfferCommand>(command =>
                {
                    captured = command;
                    return (ErrorOr<HeldOrderResult>)
                        new HeldOrderResult(
                            aggregateId,
                            "ord_demo",
                            DateTimeOffset.Parse("2026-10-23T12:00:00Z")
                        );
                });
                break;
            case "confirm":
                _fixture.Bus.OnCapture<ConfirmOrderCommand>(command =>
                {
                    captured = command;
                    return (ErrorOr<ConfirmedOrderResult>)
                        new ConfirmedOrderResult(aggregateId, "Confirmed", "payment_demo");
                });
                break;
        }

        using var request = ReadmeExamples.CreateRequest(id);
        if (id is "hold" or "confirm")
        {
            request.Headers.Add(TestAuthHandler.UserIdHeader, ReadmeExamples.UserId.ToString());
            request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        }
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(id == "quote" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        _fixture.Bus.InvocationCount.ShouldBe(1);
        captured.ShouldNotBeNull();
        switch (captured)
        {
            case SearchFlightsQuery search:
                search.Criteria.Origin.Value.ShouldBe("LED");
                search.Criteria.Destination.Value.ShouldBe("DME");
                search.Criteria.PassengerCount.ShouldBe(1);
                search.Criteria.DepartureDate.ShouldBe(new DateOnly(2026, 10, 23));
                search.Criteria.ReturnDate.ShouldBeNull();
                search.Criteria.CabinClass.Code.ShouldBe("economy");
                break;
            case NlSearchQuery nl:
                nl.Query.ShouldBe("из Москвы в Санкт-Петербург 2026-10-23");
                break;
            case QuoteOfferCommand quote:
                quote.ProviderOfferRef.ShouldBe("offer_demo");
                quote.Provider.Value.ShouldBe("duffel");
                break;
            case HoldOfferCommand hold:
                hold.AggregateId.ShouldBe(aggregateId);
                hold.UserId.ShouldBe(ReadmeExamples.UserId);
                hold.Passenger.GivenName.ShouldBe("Ivan");
                hold.Passenger.FamilyName.ShouldBe("Ivanov");
                hold.Passenger.DateOfBirth.ShouldBe(new DateOnly(1990, 1, 15));
                hold.Passenger.Gender.Code.ShouldBe("male");
                hold.Passenger.Email.ShouldBe("ivan@example.test");
                hold.Passenger.Phone.Value.ShouldBe("+79001234567");
                break;
            case ConfirmOrderCommand confirm:
                confirm.AggregateId.ShouldBe(aggregateId);
                confirm.UserId.ShouldBe(ReadmeExamples.UserId);
                break;
            default:
                throw new InvalidOperationException("Unexpected example command.");
        }
    }

    [Fact]
    public async Task Catalog_SSE_route_preserves_auth_and_order_identity()
    {
        _fixture.SseRegistry.Owner = ReadmeExamples.UserId;
        _fixture.SseRegistry.OnRegister = channel => channel.Writer.TryComplete();
        using var request = ReadmeExamples.CreateRequest("sse");
        request.Headers.Add(TestAuthHandler.UserIdHeader, ReadmeExamples.UserId.ToString());
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        _fixture.SseRegistry.LookupCount.ShouldBe(1);
        _fixture.SseRegistry.Unregistered.ShouldBe(1);
    }

    [Fact]
    public void Old_or_incomplete_README_fields_fail_strict_DTO_validation()
    {
        var search = JsonNode.Parse(ReadmeExamples.ResolvedBody("search"))!.AsObject();
        search.Remove("passengerCount");
        search["passengers"] = new JsonArray(new JsonObject { ["type"] = "adult" });
        Should.Throw<JsonException>(() =>
            ReadmeExamples.ValidateBody("search", search.ToJsonString())
        );

        foreach (var oldField in new[] { "firstName", "passportNumber" })
        {
            var hold = JsonNode.Parse(ReadmeExamples.ResolvedBody("hold"))!.AsObject();
            hold["passengers"]![0]!.AsObject()[oldField] = "legacy";
            Should.Throw<JsonException>(() =>
                ReadmeExamples.ValidateBody("hold", hold.ToJsonString())
            );
        }

        var missing = JsonNode.Parse(ReadmeExamples.ResolvedBody("hold"))!.AsObject();
        missing["passengers"]![0]!.AsObject().Remove("givenName");
        Should.Throw<JsonException>(() =>
            ReadmeExamples.ValidateBody("hold", missing.ToJsonString())
        );
    }
}
