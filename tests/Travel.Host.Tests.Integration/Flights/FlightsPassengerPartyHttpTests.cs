using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsPassengerPartyHttpTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture fixture;
    private static readonly Guid Revision = Guid.Parse("00000000-0000-0000-0000-000000000111");
    private const string Email = "distinctive-pii@example.test";

    public FlightsPassengerPartyHttpTests(FlightsApiFixture fixture)
    {
        this.fixture = fixture;
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
        fixture.PassengerPartyProtector.Reset();
        fixture.Logs.Messages.Clear();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Whole_party_is_encrypted_once_before_bus_and_binds_exact_context(int count)
    {
        var owner = Guid.NewGuid();
        var body = Body(count);
        var aggregate = body["aggregateId"]!.GetValue<Guid>();
        HoldOfferCommand? captured = null;
        fixture.Bus.OnCapture<HoldOfferCommand>(command =>
        {
            fixture.PassengerPartyProtector.ProtectCount.ShouldBe(1);
            captured = command;
            return (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(
                    command.AggregateId,
                    "ord_fictional",
                    DateTimeOffset.UtcNow.AddMinutes(10)
                );
        });
        using var request = Request(body, owner);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured.AggregateId.ShouldBe(aggregate);
        captured.UserId.ShouldBe(owner);
        captured.QuoteRevision.ShouldBe(Revision);
        captured.PassengerCount.ShouldBe(count);
        var context = new BookingPassengerPartyProtectionContext(aggregate, owner, Revision, count);
        fixture.PassengerPartyProtector.LastContext.ShouldBe(context);
        var decrypted = TestPii.PartyProtector.Unprotect(context, captured.ProtectedPassengerParty);
        decrypted.IsError.ShouldBeFalse();
        decrypted.Value.Count.ShouldBe(count);
        decrypted
            .Value.Select(p => p.Id.Value)
            .ShouldBe(TestPii.Binding(count).Slots.Select(p => p.Id.Value));
        foreach (var passenger in decrypted.Value)
            passenger.Details.Passenger.Email.ShouldBe(Email);
        foreach (
            var wrong in new[]
            {
                context with
                {
                    AggregateId = Guid.NewGuid(),
                },
                context with
                {
                    OwnerUserId = Guid.NewGuid(),
                },
                context with
                {
                    QuoteRevision = Guid.NewGuid(),
                },
                context with
                {
                    PassengerCount = count == 9 ? 8 : count + 1,
                },
            }
        )
            TestPii
                .PartyProtector.Unprotect(wrong, captured.ProtectedPassengerParty)
                .IsError.ShouldBeTrue();
        AssertNoPii(JsonSerializer.Serialize(captured));
        AssertNoPii(captured.ToString());
        AssertNoPii(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        AssertNoPii(string.Join('\n', fixture.Logs.Messages));
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("slot")]
    [InlineData("title")]
    [InlineData("null-party")]
    [InlineData("null-passenger")]
    public async Task Missing_binding_fails_before_protection_or_bus(string missing)
    {
        var body = Body(1);
        switch (missing)
        {
            case "revision":
                body.Remove("quoteRevision");
                break;
            case "slot":
                body["passengers"]![0]!.AsObject().Remove("bookingPassengerId");
                break;
            case "title":
                body["passengers"]![0]!.AsObject().Remove("title");
                break;
            case "null-party":
                body["passengers"] = null;
                break;
            case "null-passenger":
                body["passengers"]![0] = null;
                break;
        }
        await RejectBeforeBus(body, HttpStatusCode.BadRequest, "Flights.QuoteBindingRequired");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Invalid_party_count_stops_before_protection_or_bus(int count) =>
        await RejectBeforeBus(Body(count), HttpStatusCode.BadRequest, "Flights.CommandInvalid");

    [Fact]
    public async Task Duplicate_ids_stop_before_protection_or_bus()
    {
        var body = Body(2);
        body["passengers"]![1]!["bookingPassengerId"] = body["passengers"]![0]![
            "bookingPassengerId"
        ]!.DeepClone();
        await RejectBeforeBus(body, HttpStatusCode.Conflict, "Flights.PassengerSlotsMismatch");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("private-invalid-id@example.test")]
    public async Task Invalid_slot_guid_stops_before_protection_or_bus(string id)
    {
        var body = Body(1);
        body["passengers"]![0]!["bookingPassengerId"] = id;
        using var request = Request(body);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain(id);
        fixture.PassengerPartyProtector.ProtectCount.ShouldBe(0);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("title", "prof", "Flights.PassengerTitleInvalid")]
    [InlineData("gender", "unspecified", "Flights.PassengerGenderInvalid")]
    [InlineData("givenName", "AAAAAAAAAAAAAAAAAAAAA", "Flights.PassengerGivenNameInvalid")]
    [InlineData("familyName", "FamilyÆ", "Flights.PassengerFamilyNameInvalid")]
    [InlineData("email", "private-input", "Flights.PassengerEmailInvalid")]
    [InlineData("phone", "+１２３４５６７８９", "Flights.PassengerPhoneInvalid")]
    [InlineData("dateOfBirth", "0001-01-01", "Flights.PassengerDateOfBirthInvalid")]
    [InlineData("dateOfBirth", "9999-01-01", "Flights.PassengerDateOfBirthFutureInvalid")]
    public async Task Raw_field_errors_expose_only_safe_slot_field_code(
        string field,
        string value,
        string code
    )
    {
        var body = Body(2);
        body["passengers"]![1]![field] = value;
        using var request = Request(body);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("type").GetString().ShouldEndWith("Flights.PassengerInvalid");
        var safe = json.RootElement.GetProperty("passengerErrors");
        safe.GetArrayLength().ShouldBe(1);
        safe[0]
            .GetProperty("bookingPassengerId")
            .GetGuid()
            .ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        safe[0].GetProperty("field").GetString().ShouldBe(field);
        safe[0].GetProperty("code").GetString().ShouldBe(code);
        safe[0]
            .EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe(["bookingPassengerId", "field", "code"]);
        AssertNoPii(text);
        text.ShouldNotContain(value);
        fixture.PassengerPartyProtector.ProtectCount.ShouldBe(0);
        fixture.Bus.InvocationCount.ShouldBe(0);
        AssertNoPii(string.Join('\n', fixture.Logs.Messages));
    }

    [Theory]
    [InlineData("Flights.QuoteRevisionMismatch", 409)]
    [InlineData("Flights.PassengerCountMismatch", 409)]
    [InlineData("Flights.PassengerSlotsMismatch", 409)]
    public async Task Handler_binding_rejections_keep_exact_http_status(string code, int status)
    {
        fixture.Bus.On<HoldOfferCommand>(
            (ErrorOr<HeldOrderResult>)Error.Conflict(code, "Safe fixture rejection")
        );
        using var request = Request(Body(2));
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(status);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.ShouldContain(code);
        AssertNoPii(text);
    }

    [Fact]
    public async Task Passenger_error_extensions_allow_only_fixed_codes_fields_valid_ids_and_63_rows()
    {
        var id = Guid.NewGuid();
        var errors = new List<Error>();
        var fields = new[]
        {
            "title",
            "givenName",
            "familyName",
            "dateOfBirth",
            "gender",
            "email",
            "phone",
        };
        var codes = new[]
        {
            "Title",
            "GivenName",
            "FamilyName",
            "AdultRequired",
            "Gender",
            "Email",
            "Phone",
        };
        for (var i = 0; i < 70; i++)
            errors.Add(SafeError($"Flights.Passenger{codes[i % 7]}Invalid", id, fields[i % 7]));
        errors.Insert(0, SafeError("Flights.ForeignInvalid", id, "email"));
        errors.Insert(0, SafeError("Flights.PassengerEmailInvalid", Guid.Empty, "email"));
        errors.Insert(0, SafeError("Flights.PassengerEmailInvalid", id, "secret"));
        errors.Insert(
            0,
            Error.Validation(
                "Flights.PassengerEmailInvalid",
                Email,
                new Dictionary<string, object>
                {
                    ["bookingPassengerId"] = id.ToString(),
                    ["field"] = "email",
                }
            )
        );
        fixture.Bus.On<HoldOfferCommand>((ErrorOr<HeldOrderResult>)errors);
        using var request = Request(Body(1));
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(text);
        var safe = json.RootElement.GetProperty("passengerErrors");
        safe.GetArrayLength().ShouldBe(63);
        foreach (var row in safe.EnumerateArray())
        {
            row.GetProperty("bookingPassengerId").GetGuid().ShouldBe(id);
            fields.ShouldContain(row.GetProperty("field").GetString()!);
            codes
                .Select(c => $"Flights.Passenger{c}Invalid")
                .ShouldContain(row.GetProperty("code").GetString()!);
            row.EnumerateObject()
                .Select(p => p.Name)
                .ShouldBe(["bookingPassengerId", "field", "code"]);
        }
        AssertNoPii(text);
        text.ShouldNotContain("secret");
        text.ShouldNotContain("Flights.ForeignInvalid");
    }

    [Fact]
    public async Task Possible_supplier_effect_remains_typed_409_with_no_local_validation_extension()
    {
        fixture.Bus.On<HoldOfferCommand>(
            (ErrorOr<HeldOrderResult>)
                Travel.Modules.Flights.Core.Errors.FlightsErrors.HoldOutcomeUnknown
        );
        using var request = Request(Body(2));
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("type")
            .GetString()
            .ShouldEndWith("Flights.HoldOutcomeUnknown");
        json.RootElement.TryGetProperty("passengerErrors", out _).ShouldBeFalse();
        AssertNoPii(text);
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task Malformed_global_details_are_not_assigned_to_an_invented_passenger_row()
    {
        fixture.Bus.On<HoldOfferCommand>(
            (ErrorOr<HeldOrderResult>)
                Error.Validation(
                    "Flights.PassengerDetailsInvalid",
                    "Safe invalid details",
                    new Dictionary<string, object>
                    {
                        ["bookingPassengerId"] = Guid.Empty,
                        ["field"] = "details",
                        ["rawValue"] = Email,
                    }
                )
        );
        using var request = Request(Body(1));
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(text);
        json.RootElement.GetProperty("type").GetString().ShouldEndWith("Flights.PassengerInvalid");
        json.RootElement.TryGetProperty("passengerErrors", out _).ShouldBeFalse();
        AssertNoPii(text);
    }

    private async Task RejectBeforeBus(JsonObject body, HttpStatusCode status, string code)
    {
        using var request = Request(body);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(status);
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldContain(code);
        fixture.PassengerPartyProtector.ProtectCount.ShouldBe(0);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    private static Error SafeError(string code, Guid id, string field) =>
        Error.Validation(
            code,
            Email,
            new Dictionary<string, object>
            {
                ["bookingPassengerId"] = id,
                ["field"] = field,
                ["rawValue"] = "FictionalGiven",
            }
        );

    private static void AssertNoPii(string text)
    {
        foreach (
            var sentinel in new[]
            {
                "FictionalGiven",
                Email,
                "FictionalFamily",
                "+12025550123",
                "1987-02-14",
            }
        )
            text.ShouldNotContain(sentinel);
    }

    internal static JsonObject Body(int count) =>
        JsonSerializer
            .SerializeToNode(
                new
                {
                    aggregateId = Guid.NewGuid(),
                    quoteRevision = Revision,
                    passengers = Enumerable
                        .Range(1, count)
                        .Select(i => new
                        {
                            bookingPassengerId = Guid.Parse(
                                $"00000000-0000-0000-0000-{i:000000000000}"
                            ),
                            title = "mr",
                            givenName = "FictionalGiven",
                            familyName = "FictionalFamily",
                            dateOfBirth = "1987-02-14",
                            gender = "female",
                            email = Email,
                            phone = "+12025550123",
                        })
                        .ToArray(),
                }
            )!
            .AsObject();

    internal static HttpRequestMessage Request(JsonObject body, Guid? owner = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(TestAuthHandler.UserIdHeader, (owner ?? Guid.NewGuid()).ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }
}
