using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class DuffelStaysRealShapeTests
{
    [Theory]
    [InlineData(98, PreparationKind.Ready)]
    [InlineData(99, PreparationKind.Failed)]
    public async Task Key_collection_keeps_complete_conditions_within_the_client_contract(
        int conditionCount,
        PreparationKind expected
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "quote.json"),
                ct
            )
        )!;
        var accommodation = json["data"]!["accommodation"]!;
        accommodation["key_collection"] = System.Text.Json.Nodes.JsonNode.Parse(
            "{\"instructions\":\"Collect keys at reception.\"}"
        );
        accommodation["rooms"]![0]!["rates"]![0]!["conditions"] =
            System.Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(
                    Enumerable
                        .Range(0, conditionCount)
                        .Select(i => new
                        {
                            title = $"Condition {i}",
                            description = "Fictional condition",
                        })
                )
            );
        var request = new StaySearch(
            HotelSearchService.Areas[0],
            new(new(2027, 1, 3), new(2027, 1, 5)),
            new(1)
        );
        var result = await DuffelStaysProviderTests
            .Provider(new(json.ToJsonString()))
            .PrepareAsync(
                new(
                    request,
                    new(
                        "duffel-stays",
                        "Test",
                        "rat_fictional",
                        DuffelStaysProviderTests.Terms() with
                        {
                            Dates = request.Dates,
                        },
                        null,
                        true,
                        null
                    )
                ),
                ct
            );
        result.Kind.ShouldBe(expected);
        if (result.Kind == PreparationKind.Ready)
            result.Stay!.Terms.Conditions.Length.ShouldBe(100);
    }

    [Fact]
    public async Task Key_collection_instructions_survive_quote_preparation_and_actual_booking()
    {
        var ct = TestContext.Current.CancellationToken;
        const string instruction =
            "Contact reception before arrival; collect keys at the fictional annex.";
        var quoteJson = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "quote.json"),
                ct
            )
        )!;
        quoteJson["data"]!["accommodation"]!["key_collection"] =
            System.Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(new { instructions = instruction })
            );
        var request = new StaySearch(
            HotelSearchService.Areas[0],
            new(new(2027, 1, 3), new(2027, 1, 5)),
            new(1)
        );
        var selected = new SelectedStay(
            request,
            new(
                "duffel-stays",
                "Test",
                "rat_fictional",
                DuffelStaysProviderTests.Terms() with
                {
                    Dates = request.Dates,
                },
                null,
                true,
                null
            )
        );
        var prepared = await DuffelStaysProviderTests
            .Provider(new(quoteJson.ToJsonString()))
            .PrepareAsync(selected, ct);
        prepared.Kind.ShouldBe(PreparationKind.Ready);
        prepared.Stay!.Terms.Conditions.ShouldContain(f => f.Text == instruction);

        var bookingJson = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "booking201.json"),
                ct
            )
        )!;
        bookingJson["data"]!["accommodation"]!["key_collection"] = quoteJson["data"]![
            "accommodation"
        ]!["key_collection"]!.DeepClone();
        var actual = await DuffelStaysProviderTests
            .Provider(new(bookingJson.ToJsonString(), HttpStatusCode.Created))
            .CreateAsync(Purchase(), ct);
        actual.Kind.ShouldBe(ObservationKind.Confirmed);
        actual.Facts!.Actual!.Conditions.ShouldContain(f => f.Text == instruction);
    }

    [Fact]
    public async Task Nullable_booking_guest_scope_retains_positive_identity_without_inventing_occupancy()
    {
        var ct = TestContext.Current.CancellationToken;
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "booking201.json"),
                ct
            )
        )!;
        json["data"]!["guest_types"] = null;
        var createHandler = new DuffelStaysProviderTests.Handler(
            json.ToJsonString(),
            HttpStatusCode.Created
        );
        var created = await DuffelStaysProviderTests
            .Provider(createHandler)
            .CreateAsync(Purchase(), ct);
        created.Kind.ShouldBe(ObservationKind.ReservedWithoutDetails);
        created.Facts!.BookingId.ShouldBe("bok_fictional");
        created.Facts.Actual.ShouldBeNull();
        var state = new ReservationState { Outcome = ReservationOutcome.Submitted };
        var now = DateTimeOffset.UtcNow;
        state.Apply(created, Purchase().Accepted, now);
        state.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        state.EverReserved.ShouldBeTrue();
        state.FirstKnownAt.ShouldBe(now);
        state.ReadUntil.ShouldBe(now.AddMinutes(5));
        state.ClaimRead(now).ShouldBeTrue();
        var readHandler = new DuffelStaysProviderTests.Handler(json.ToJsonString());
        var observed = await DuffelStaysProviderTests
            .Provider(readHandler)
            .ReadAsync("bok_fictional", ct);
        observed.Kind.ShouldBe(ObservationKind.ReservedWithoutDetails);
        observed.CurrentRead.ShouldBeTrue();
        observed.Facts!.Actual.ShouldBeNull();
        state.Apply(observed, Purchase().Accepted, now.AddSeconds(1));
        state.EverReserved.ShouldBeTrue();
        state.Outcome.ShouldBe(ReservationOutcome.ReservedAwaitingDetails);
        state.Facts!.Actual.ShouldBeNull();
        state.ReadsClaimed.ShouldBe(1);
        state.FirstKnownAt.ShouldBe(now);
        state.ReadUntil.ShouldBe(now.AddMinutes(5));
        createHandler.Calls.ShouldBe(1);
        readHandler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Dot_segment_source_identity_never_reaches_a_booking_or_list_HTTP_path(
        string id
    )
    {
        var handler = new DuffelStaysProviderTests.Handler("{}");
        var provider = DuffelStaysProviderTests.Provider(handler);
        (
            await provider.ReadAsync(id, TestContext.Current.CancellationToken)
        ).Attributable.ShouldBeFalse();
        var request = DuffelStaysProviderTests.Request();
        (
            await provider.LoadRatesAsync(
                new(
                    request,
                    new(
                        "duffel-stays",
                        "Test",
                        id,
                        DuffelStaysProviderTests.Terms(),
                        null,
                        true,
                        null
                    )
                ),
                TestContext.Current.CancellationToken
            )
        ).Kind.ShouldBe(ExecutionKind.Failed);
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Two_adult_occupancy_does_not_invent_board_guest_coverage()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "rates.json"),
                TestContext.Current.CancellationToken
            )
        )!;
        var data = json["data"]!;
        data["guests"]!
            .AsArray()
            .Add(System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"adult\"}"));
        var rate = data["accommodation"]!["rooms"]![0]!["rates"]![0]!;
        rate["board_type"] = "breakfast";
        rate["benefits"] = System.Text.Json.Nodes.JsonNode.Parse(
            "[{\"type\":\"breakfast\",\"description\":\"Breakfast for one guest only\"}]"
        );
        var request = new StaySearch(
            HotelSearchService.Areas[0],
            new(new(2027, 1, 3), new(2027, 1, 5)),
            new(2)
        );
        var provider = DuffelStaysProviderTests.Provider(new(json.ToJsonString()));
        var result = await provider.LoadRatesAsync(
            new(
                request,
                new(
                    "duffel-stays",
                    "Test",
                    "srr_fictional",
                    DuffelStaysProviderTests.Terms() with
                    {
                        Occupancy = new(2),
                    },
                    null,
                    false,
                    null
                )
            ),
            TestContext.Current.CancellationToken
        );
        result.Kind.ShouldBe(ExecutionKind.Complete);
        var board = result.Items.Single().Terms.Board;
        board[0].Scope.ShouldContain("не указано");
        board[0].Scope.ShouldNotContain("2");
        board[1].Text.ShouldBe("Breakfast for one guest only");
    }

    [Fact]
    public async Task Full_create_deadline_uses_injected_clock_and_does_not_retry()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new HangingHandler();
        var settings = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_fictional_only",
                WebhookSecret = "fictional-webhook-secret",
                CreateTimeoutSeconds = 140,
            }
        );
        var provider = new DuffelStaysProvider(new(handler), settings, new(settings, clock), clock);
        var pending = provider.CreateAsync(Purchase(), TestContext.Current.CancellationToken);
        await handler.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(139));
        pending.IsCompleted.ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        (await pending).Kind.ShouldBe(ObservationKind.Unknown);
        handler.Count.ShouldBe(1);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Count++;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }

    [Theory]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"results\":[null]}}")]
    public async Task Missing_or_null_search_shape_is_safe_failure(string body)
    {
        var result = await DuffelStaysProviderTests
            .Provider(new(body))
            .SearchAsync(DuffelStaysProviderTests.Request(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(ExecutionKind.Failed);
        result.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("rates", "null-rates")]
    [InlineData("rates", "null-room")]
    [InlineData("rates", "null-rate")]
    [InlineData("rates", "null-guest")]
    [InlineData("rates", "too-many-conditions")]
    [InlineData("rates", "too-many-benefits")]
    [InlineData("quote", "null-rates")]
    [InlineData("quote", "null-guest")]
    [InlineData("booking201", "null-rates")]
    [InlineData("booking201", "null-guest")]
    [InlineData("rates", "missing-before")]
    [InlineData("rates", "null-before")]
    [InlineData("booking201", "missing-status")]
    [InlineData("booking201", "null-status")]
    [InlineData("booking201", "unknown-status")]
    [InlineData("booking201", "nullable-scope-missing-status")]
    [InlineData("booking201", "nullable-scope-null-rates")]
    [InlineData("booking201", "nullable-scope-invalid-date")]
    [InlineData("booking201", "missing-guest-types")]
    public async Task Malformed_nested_material_facts_never_throw_or_become_success(
        string fixture,
        string mutation
    )
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".json"),
                TestContext.Current.CancellationToken
            )
        )!;
        var data = json["data"]!;
        var rooms = data["accommodation"]!["rooms"]!.AsArray();
        switch (mutation)
        {
            case "nullable-scope-missing-status":
                data["guest_types"] = null;
                data.AsObject().Remove("status");
                break;
            case "nullable-scope-null-rates":
                data["guest_types"] = null;
                rooms[0]!["rates"] = null;
                break;
            case "nullable-scope-invalid-date":
                data["guest_types"] = null;
                data["check_in_date"] = "invalid";
                break;
            case "missing-guest-types":
                data.AsObject().Remove("guest_types");
                break;
            case "null-rates":
                rooms[0]!["rates"] = null;
                break;
            case "null-room":
                rooms[0] = null;
                break;
            case "null-rate":
                rooms[0]!["rates"]![0] = null;
                break;
            case "null-guest":
                data[fixture == "booking201" ? "guest_types" : "guests"]![0] = null;
                break;
            case "missing-before":
                rooms[0]!["rates"]![0]!["cancellation_timeline"] =
                    System.Text.Json.Nodes.JsonNode.Parse(
                        "[{\"refund_amount\":\"10.00\",\"currency\":\"GBP\"}]"
                    );
                break;
            case "null-before":
                rooms[0]!["rates"]![0]!["cancellation_timeline"] =
                    System.Text.Json.Nodes.JsonNode.Parse(
                        "[{\"refund_amount\":\"10.00\",\"currency\":\"GBP\",\"before\":null}]"
                    );
                break;
            case "missing-status":
                data.AsObject().Remove("status");
                break;
            case "null-status":
                data["status"] = null;
                break;
            case "unknown-status":
                data["status"] = "invented";
                break;
            case "too-many-conditions":
                rooms[0]!["rates"]![0]!["conditions"] = new System.Text.Json.Nodes.JsonArray(
                    Enumerable
                        .Range(0, 101)
                        .Select(_ =>
                            System.Text.Json.Nodes.JsonNode.Parse(
                                "{\"title\":\"Fictional\",\"description\":\"Condition\"}"
                            )
                        )
                        .ToArray()
                );
                break;
            case "too-many-benefits":
                rooms[0]!["rates"]![0]!["benefits"] = new System.Text.Json.Nodes.JsonArray(
                    Enumerable
                        .Range(0, 101)
                        .Select(_ =>
                            System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"Fictional\"}")
                        )
                        .ToArray()
                );
                break;
        }
        var provider = DuffelStaysProviderTests.Provider(
            new(
                json.ToJsonString(),
                fixture == "booking201" ? HttpStatusCode.Created : HttpStatusCode.OK
            )
        );
        var selection = new SelectedStay(
            DuffelStaysProviderTests.Request(),
            new(
                "duffel-stays",
                "Test",
                "rat_fictional",
                DuffelStaysProviderTests.Terms() with
                {
                    Property = DuffelStaysProviderTests.Terms().Property with
                    {
                        Id = data["accommodation"]!["id"]!.GetValue<string>(),
                    },
                },
                null,
                true,
                null
            )
        );
        if (fixture == "rates")
            (
                await provider.LoadRatesAsync(selection, TestContext.Current.CancellationToken)
            ).Kind.ShouldBe(ExecutionKind.Failed);
        else if (fixture == "quote")
            (
                await provider.PrepareAsync(selection, TestContext.Current.CancellationToken)
            ).Kind.ShouldBe(PreparationKind.Failed);
        else
            (
                await provider.CreateAsync(Purchase(), TestContext.Current.CancellationToken)
            ).Kind.ShouldBe(ObservationKind.Unknown);
    }

    [Fact]
    public async Task Documented_search_rates_quote_create_and_get_shapes_preserve_exact_terms()
    {
        var handler = new FixtureHandler();
        var settings = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_fictional_only",
                WebhookSecret = "fictional-webhook-secret",
                ForegroundConcurrency = 50,
            }
        );
        var provider = new DuffelStaysProvider(
            new(handler),
            settings,
            new(settings, TimeProvider.System),
            TimeProvider.System
        );
        var request = new StaySearch(
            HotelSearchService.Areas[0],
            new(new(2027, 1, 3), new(2027, 1, 5)),
            new(1)
        );
        var search = await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        search.Kind.ShouldBe(ExecutionKind.Complete);
        search.Items.Single().Terms.Total.Amount.ShouldBe(120.25m);
        var rates = await provider.LoadRatesAsync(
            new(request, search.Items.Single()),
            TestContext.Current.CancellationToken
        );
        rates.Kind.ShouldBe(ExecutionKind.Complete);
        var rate = rates.Items.Single();
        rate.Purchasable.ShouldBeTrue();
        rate.Terms.Cancellation.Kind.ShouldBe("NonRefundable");
        rate.Terms.MandatoryLocalPayments!.Single().Amount.ShouldBe(0);
        var prepare = await provider.PrepareAsync(
            new(request, rate),
            TestContext.Current.CancellationToken
        );
        prepare.Kind.ShouldBe(PreparationKind.Ready);
        var outcome = await provider.CreateAsync(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                prepare.Stay!.Terms,
                prepare.Stay.Context,
                new(
                    [new("Fictional", "Traveller")],
                    "fictional@example.invalid",
                    "+44000000000",
                    true,
                    true
                )
            ),
            TestContext.Current.CancellationToken
        );
        outcome.Kind.ShouldBe(ObservationKind.Confirmed);
        outcome.Facts!.HotelReference.ShouldBeNull();
        outcome.Facts.Actual!.Total.Amount.ShouldBe(120.25m);
        outcome.RequestReference.ShouldBe("req_fictional_create");
        ReservationState.TermsMatch(prepare.Stay.Terms, outcome.Facts.Actual).ShouldBeTrue();
        var observed = await provider.ReadAsync(
            "bok_fictional",
            TestContext.Current.CancellationToken
        );
        observed.Kind.ShouldBe(ObservationKind.Confirmed);
        observed.CurrentRead.ShouldBeTrue();
        handler.CreateCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("{}", ObservationKind.Unknown)]
    [InlineData("<html>gateway</html>", ObservationKind.Unknown)]
    [InlineData(
        "{\"data\":{\"message\":\"The booking has been confirmed. It will appear in the system soon.\"}}",
        ObservationKind.ReservedWithoutDetails
    )]
    public async Task Only_complete_documented_200_envelope_establishes_reservation(
        string body,
        ObservationKind expected
    )
    {
        var provider = DuffelStaysProviderTests.Provider(new(body));
        var result = await provider.CreateAsync(Purchase(), TestContext.Current.CancellationToken);
        result.Kind.ShouldBe(expected);
    }

    [Fact]
    public async Task Full_provider_503_envelope_is_no_creation_but_proxy_503_is_unknown()
    {
        const string error =
            "{\"errors\":[{\"type\":\"api_error\",\"code\":\"internal_server_error\",\"title\":\"Unavailable\",\"message\":\"Fictional\",\"documentation_url\":\"https://duffel.com/docs/api/overview/response-handling\"}],\"meta\":{\"request_id\":\"req_fictional\",\"status\":503}}";
        var source = await DuffelStaysProviderTests
            .Provider(new(error, HttpStatusCode.ServiceUnavailable))
            .CreateAsync(Purchase(), TestContext.Current.CancellationToken);
        source.Kind.ShouldBe(ObservationKind.NotCreated);
        source.RequestReference.ShouldBe("req_fictional");
        var proxy = await DuffelStaysProviderTests
            .Provider(new("Service unavailable", HttpStatusCode.ServiceUnavailable))
            .CreateAsync(Purchase(), TestContext.Current.CancellationToken);
        proxy.Kind.ShouldBe(ObservationKind.Unknown);
    }

    private static CreateStay Purchase() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DuffelStaysProviderTests.Terms(),
            new(1, "{\"QuoteId\":\"quo_fictional\",\"RateId\":\"rat_fictional\"}"),
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            )
        );

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int CreateCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri!.AbsolutePath;
            var file =
                path == "/stays/search" ? "search.json"
                : path.Contains("fetch_all_rates", StringComparison.Ordinal) ? "rates.json"
                : path == "/stays/quotes" ? "quote.json"
                : request.Method == HttpMethod.Get ? "booking-get.json"
                : "booking201.json";
            if (file == "booking201.json")
            {
                CreateCount++;
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                body.ShouldContain("quo_fictional");
                body.ShouldNotContain("payment");
            }
            return new(file == "booking201.json" ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = new StringContent(
                    await File.ReadAllTextAsync(
                        Path.Combine(AppContext.BaseDirectory, "Fixtures", file),
                        cancellationToken
                    ),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
