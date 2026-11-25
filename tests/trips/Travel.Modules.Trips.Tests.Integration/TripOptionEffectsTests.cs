using System.Net;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Wolverine;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripOptionEffectsTests(ITestOutputHelper output)
{
    private const string Endpoint = "/api/trips/option-preview";
    private const string Budget = "83472.19";
    private const string PrivateMarker = "private-option-effects-sentinel";
    private const string Brief =
        $$$"""{"brief":{"originId":"moscow","destinationId":"saint-petersburg","startDate":"2026-10-28","endDate":"2026-10-30","adults":2,"currency":"RUB","totalBudget":"{{{Budget}}}","interests":["architecture"],"pace":"relaxed"}}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Positive_controls_observe_every_connected_boundary_before_any_external_dispatch()
    {
        var probe = new TripOptionEffectsProbe();
        await using var fixture = await Start(
            probe,
            source: provider => new PositiveControlEvidence(
                provider.GetRequiredService<IServiceScopeFactory>()
            )
        );
        using var created = await Send(
            fixture,
            "create-control",
            TripsHttpTests.CreateJson,
            Ct,
            "/api/trips"
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var createCache = await probe.Completion("create-control", Ct);
        probe.Counts("create-control")["workspace.TryCreateAsync"].ShouldBe(1);
        fixture.Store.Rows.Count.ShouldBe(1);
        Emit(
            "create-control",
            created,
            createCache,
            probe.Counts("create-control"),
            fixture.Store.Rows.Count
        );
        // Inject the deliberate effects at the real preview's evidence-read boundary. Both
        // positive HTTP requests are correlated by precisely the same middleware as the matrix.
        using var controlled = await Send(fixture, "commercial-control", Brief, Ct);
        controlled.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var cache = await probe.Completion("commercial-control", Ct);
        var counts = probe.Counts("commercial-control");
        counts
            .Keys.Order()
            .ShouldBe(
                new[]
                {
                    "IFlightsTripsOrderAccess.ListCandidatesAsync",
                    "IHotelsTripsReservationAccess.ReadAsync",
                    "IPaymentGateway.AuthorizeAsync",
                    "IFlightBookingProvider.CancelOrderAsync",
                    "IFlightSearchProvider.SearchAsync",
                    "IHotelBookingProvider.PrepareAsync",
                    "IHotelSearchProvider.SearchAsync",
                    "messageBus",
                    "modelPort.ParseAsync",
                    "modelRpc",
                    "outboundHttp",
                }.Order()
            );
        counts.Values.ShouldAllBe(n => n == 1);
        probe.Counts("outside-request").ShouldBeEmpty();
        Emit("commercial-control", controlled, cache, counts, fixture.Store.Rows.Count);
    }

    private static async Task ExerciseCommercialBoundaries(
        IServiceProvider services,
        CancellationToken ct
    )
    {
        var owner = TripsHttpFixture.Owner;
        var date = new DateOnly(2026, 10, 28);
        var currency = CurrencyCode.Create("RUB").Value;
        var search = SearchCriteria
            .Create(
                IataCode.Create("SVO").Value,
                IataCode.Create("LED").Value,
                date,
                date.AddDays(2),
                1,
                CabinClass.Economy,
                currency
            )
            .Value;
        var stay = new StaySearch(
            new("fictional", 59, 30, 1, true),
            new(date, date.AddDays(1)),
            new(1)
        );
        var terms = new StayTerms(
            new("id", "Fictional", null, null, null),
            "room",
            "rate",
            stay.Dates,
            stay.Occupancy,
            new(100, "RUB"),
            [],
            [],
            new("unknown", []),
            [],
            true,
            "test"
        );
        var hotelOffer = new HotelOffer("fictional", "Test", "control", terms, null, true, null);
        await Block(() =>
            services
                .GetRequiredService<IFlightsTripsOrderAccess>()
                .ListCandidatesAsync(owner, 0, ct)
        );
        await Block(() =>
            services
                .GetRequiredService<IHotelsTripsReservationAccess>()
                .ReadAsync(owner, Guid.NewGuid(), ct)
        );
        await Block(() =>
            services
                .GetRequiredService<IPaymentGateway>()
                .AuthorizeAsync(Money.Create(100, currency).Value, "fictional", ct)
        );
        await Block(() =>
            services.GetRequiredService<IFlightBookingProvider>().CancelOrderAsync("fictional", ct)
        );
        await Block(() =>
            services.GetRequiredService<IFlightSearchProvider>().SearchAsync(search, ct)
        );
        await Block(() =>
            services
                .GetRequiredService<IHotelBookingProvider>()
                .PrepareAsync(new(stay, hotelOffer), ct)
        );
        await Block(() =>
            services.GetRequiredService<IHotelSearchProvider>().SearchAsync(stay, ct)
        );
        await Block(() => services.GetRequiredService<IMessageBus>().InvokeAsync(new object(), ct));
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient("duffel");
        await Block(() => client.GetAsync("https://fictional.invalid/guarded", ct));
        // The real model client maps the test transport refusal to its ordinary unavailable outcome.
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var model = await services
            .GetRequiredService<ITripBriefExtractionClient>()
            .ParseAsync(
                new(
                    Guid.NewGuid(),
                    "test",
                    "fictional",
                    "ru",
                    now,
                    "Europe/Moscow",
                    now.AddMinutes(1)
                ),
                ct
            );
        model.FirstError.Code.ShouldBe(TripBriefErrors.Unavailable.Code);
    }

    [Theory]
    [InlineData("option", 200)]
    [InlineData("noMatch", 200)]
    [InlineData("unsupported", 200)]
    [InlineData("invalid", 400)]
    [InlineData("unauthenticated", 401)]
    [InlineData("forbidden", 403)]
    [InlineData("disabled", 404)]
    [InlineData("oversized", 413)]
    [InlineData("withdrawn", 503)]
    public async Task Every_completed_outcome_has_zero_observed_effects_and_private_diagnostics(
        string label,
        int status
    )
    {
        var probe = new TripOptionEffectsProbe();
        await using var fixture = await Start(
            probe,
            enabled: label != "disabled",
            withdrawn: label == "withdrawn"
        );
        if (label == "unauthenticated")
            fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        if (label == "forbidden")
            fixture.Client.DefaultRequestHeaders.Add(
                "X-Trips-OtherSubject",
                Guid.NewGuid().ToString()
            );
        var body = label switch
        {
            "noMatch" => Brief.Replace("10-28", "10-29"),
            "unsupported" => Brief.Replace("\"adults\":2", "\"adults\":3"),
            "invalid" => Brief.Replace("architecture", PrivateMarker),
            "oversized" => Brief.PadRight(4097),
            _ => Brief,
        };
        using var response = await Send(fixture, label, body, Ct);
        ((int)response.StatusCode).ShouldBe(status);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var content = await response.Content.ReadAsStringAsync(Ct);
        if (status == 200)
        {
            using var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("outcome").GetString().ShouldBe(label);
        }
        else
            AssertPrivate(content, body);
        await AssertQuiet(fixture, probe, label, body, response, status == 200 ? content : null);
    }

    [Fact]
    public async Task Busy_cancelled_and_deadline_requests_are_individually_observed_through_pipeline_completion()
    {
        var probe = new TripOptionEffectsProbe();
        var slow = new SlowEvidence();
        await using var fixture = await Start(probe, source: _ => slow);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var cancelled = Send(fixture, "cancelled", Brief, cancel.Token);
        var deadline = Send(fixture, "deadline", Brief, Ct);
        await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var busy = await Send(fixture, "busy", Brief, Ct);
        busy.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        busy.Headers.CacheControl!.NoStore.ShouldBeTrue();
        AssertPrivate(await busy.Content.ReadAsStringAsync(Ct), Brief);
        await AssertQuiet(fixture, probe, "busy", Brief, busy);
        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await cancelled);
        await AssertQuiet(fixture, probe, "cancelled", Brief, null);
        using var timedOut = await deadline;
        timedOut.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        timedOut.Headers.CacheControl!.NoStore.ShouldBeTrue();
        AssertPrivate(await timedOut.Content.ReadAsStringAsync(Ct), Brief);
        await AssertQuiet(fixture, probe, "deadline", Brief, timedOut);
    }

    private async Task AssertQuiet(
        TripsHttpFixture fixture,
        TripOptionEffectsProbe probe,
        string label,
        string requestBody,
        HttpResponseMessage? response,
        string? successBody = null
    )
    {
        var completion = await probe.Completion(label, Ct);
        completion.CacheControl.ShouldContain("no-store");
        completion.Path.ShouldBe(Endpoint);
        completion.Method.ShouldBe("POST");
        completion.RequestAborted.ShouldBe(response is null);
        var counts = probe.Counts(label);
        counts.ShouldBeEmpty();
        probe.Counts("outside-request").ShouldBeEmpty();
        fixture.Store.Rows.ShouldBeEmpty();
        fixture.Logs.Overflow.ShouldBeFalse();
        var logs = string.Join("\n", fixture.Logs.Values);
        AssertPrivate(logs, requestBody);
        if (successBody is not null)
        {
            using var actual = JsonDocument.Parse(successBody);
            if (
                actual.RootElement.GetProperty("option") is
                { ValueKind: JsonValueKind.Object } option
            )
                logs.ShouldNotContain(
                    option.GetProperty("price").GetProperty("knownSubtotal").GetString()!
                );
        }
        Emit(label, response, completion, counts, fixture.Store.Rows.Count);
    }

    private void Emit(
        string label,
        HttpResponseMessage? response,
        TripOptionEffectsProbe.ServerCompletion completion,
        Dictionary<string, int> counts,
        int storedRows
    )
    {
        output.WriteLine(
            "TRIPS_OPTION_EFFECTS "
                + JsonSerializer.Serialize(
                    new
                    {
                        label,
                        httpStatus = (int?)response?.StatusCode,
                        serverCompletion = completion,
                        observedCalls = counts
                            .OrderBy(c => c.Key, StringComparer.Ordinal)
                            .ToDictionary(),
                        totalCalls = counts.Values.Sum(),
                        storedRows,
                    },
                    JsonSerializerOptions.Web
                )
        );
    }

    private static void AssertPrivate(string diagnostic, string requestBody)
    {
        using var actual = JsonDocument.Parse(requestBody);
        var brief = actual.RootElement.GetProperty("brief");
        var markers = new[]
        {
            PrivateMarker,
            TripsHttpFixture.Owner.ToString(),
            brief.GetProperty("totalBudget").GetString()!,
            brief.GetProperty("startDate").GetString()!,
            brief.GetProperty("endDate").GetString()!,
        }.Concat(brief.GetProperty("interests").EnumerateArray().Select(i => i.GetString()!));
        foreach (var marker in markers)
            diagnostic.ShouldNotContain(marker);
    }

    private static Task<TripsHttpFixture> Start(
        TripOptionEffectsProbe probe,
        bool enabled = true,
        bool withdrawn = false,
        Func<IServiceProvider, IItineraryFactSource>? source = null
    ) =>
        TripsHttpFixture.Start(
            itinerary: true,
            environment: "Testing",
            optionPreview: enabled,
            optionProfile: "spb-option",
            optionCatalogue: "A",
            withdrawn: withdrawn,
            configureAfterWolverine: services =>
            {
                probe.Register(services);
                if (source is not null)
                    services.AddSingleton(provider => new TripOptionService(
                        provider.GetRequiredService<TripOptionScenario>(),
                        source(provider),
                        provider.GetRequiredService<ItineraryAdmissionGate>(),
                        () => false
                    ));
            }
        );

    private static async Task<HttpResponseMessage> Send(
        TripsHttpFixture fixture,
        string label,
        string body,
        CancellationToken ct,
        string endpoint = Endpoint
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TripOptionEffectsProbe.Header, label);
        request.Headers.Add("X-Fictional-Private-Value", PrivateMarker);
        return await fixture.Client.SendAsync(request, ct);
    }

    private static Task Block(Func<Task> action) =>
        Should.ThrowAsync<TripOptionEffectsProbe.BlockedTestEffect>(action);

    private sealed class PositiveControlEvidence(IServiceScopeFactory scopes) : IItineraryFactSource
    {
        public async Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken cancellationToken = default
        )
        {
            using var scope = scopes.CreateScope();
            await ExerciseCommercialBoundaries(scope.ServiceProvider, cancellationToken);
            return Error.Failure(
                "Trips.ItineraryUnavailable",
                "Fictional positive control completed."
            );
        }
    }

    private sealed class SlowEvidence : IItineraryFactSource
    {
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public async Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref count) == 2)
                Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException(
                "The test source only completes through cancellation."
            );
        }
    }
}
