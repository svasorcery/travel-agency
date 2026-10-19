using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Api.Composition;
using Travel.Modules.Hotels.Api.Endpoints;
using Travel.Modules.Hotels.Api.Middleware;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

// An explicitly selected test harness. It never composes Travel.Host, EF, an initializer,
// Keycloak, certificate stores or a socket-backed supplier handler.
public sealed class HotelM2MemoryFixture : IAsyncDisposable
{
    public const string Owner = "785e39a4-b13f-4fd8-835b-1ab619cd0e14";
    private readonly WebApplication app;
    private readonly OwnedDuffelWire duffel = new();
    private readonly OwnedHotelbedsWire hotelbeds = new();
    public OwnedHotelMemoryStore Store { get; } = new();
    public HotelbedsOptions HotelbedsConfiguration { get; private set; } = null!;
    public HotelsOptions DuffelConfiguration { get; private set; } = null!;
    public HotelReviewedMap ReviewedMap { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => app.Services;
    public TimeProvider Clock { get; }

    public int Creates(string source) => source == "hotelbeds" ? hotelbeds.Creates : duffel.Creates;

    public int Reads(string source) => source == "hotelbeds" ? hotelbeds.Reads : duffel.Reads;

    public int Preparations(string source) =>
        source == "hotelbeds" ? hotelbeds.Preparations : duffel.Preparations;

    public void SetHotelbedsReadScenario(bool unavailable, decimal? actualTotal = null)
    {
        hotelbeds.ReadsUnavailable = unavailable;
        hotelbeds.ReadTotal = actualTotal;
    }

    public void ReturnHotelbedsReferenceWithoutDetails() => hotelbeds.ReservedWithoutDetails = true;

    public void ReturnDuffelReferenceWithoutDetails() => duffel.ReservedWithoutDetails = true;

    private HotelM2MemoryFixture(int? port, TimeProvider? clock)
    {
        Clock = clock ?? TimeProvider.System;
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { EnvironmentName = "Testing", Args = [] }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        if (port is null)
            builder.WebHost.UseTestServer();
        else
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:" + port.Value);
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.UnmappedMemberHandling = System
                .Text
                .Json
                .Serialization
                .JsonUnmappedMemberHandling
                .Disallow
        );
        builder
            .Services.AddAuthentication("owned")
            .AddScheme<AuthenticationSchemeOptions, OwnedHotelAuthentication>("owned", _ => { });
        HotelsModule.ConfigureAuthorization(builder.Services);
        var settings = Options.Create(
            new HotelsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                ReceiverEnabled = true,
                ApiKey = "duffel_test_owned_memory",
                OrganisationId = "org_owned_transport",
                WebhookSecret = "owned-fictional-secret",
                Aggregation = new()
                {
                    Enabled = true,
                    Fictional = true,
                    AccountAlias = "duffel-owned",
                    ConfigurationRevision = "owned-1",
                    ApprovalRevision = "owned-1",
                },
            }
        );
        DuffelConfiguration = settings.Value;
        var hbSettings = Options.Create(
            new HotelbedsOptions
            {
                Enabled = true,
                BookingEnabled = true,
                Fictional = true,
                ApiKey = "owned-fictional-hbx-key",
                Secret = "owned-fictional-hbx-secret",
                AccountAlias = "hotelbeds-owned",
                ConfigurationRevision = "owned-1",
                ApprovalRevision = "owned-1",
            }
        );
        HotelbedsConfiguration = hbSettings.Value;
        var capacity = new HotelCapacity(settings, Clock);
        var du = new DuffelStaysProvider(
            new DuffelStaysFictionalHttpClient(duffel),
            settings,
            capacity,
            Clock
        );
        var hb = new HotelbedsStayProvider(
            new HotelbedsFictionalHttpClient(hotelbeds),
            hbSettings,
            capacity,
            Clock,
            settings
        );
        var now = Clock.GetUtcNow();
        var left = new HotelProperty(
            "acc_fictional",
            "Owned Fictional House",
            "1 Owned Fictional Square",
            "3",
            "Duffel accommodation rating"
        );
        var right = new HotelProperty(
            "900001",
            "Owned Fictional House",
            "1 Owned Fictional Square",
            null,
            null
        );
        var reviewed = new HotelReviewedMap([
            new(
                "owned-pair",
                "owned-1",
                new(du.CurrentSource.Identity, left.Id),
                new(hb.CurrentSource.Identity, right.Id),
                "Project-owned independent fictional building/address record",
                "fictional-fixture-owner",
                "Same owned fictional building",
                now.AddDays(-1),
                now.AddHours(1),
                "Fictional",
                HotelPropertyMatching.PropertyFingerprint(left),
                HotelPropertyMatching.PropertyFingerprint(right)
            ),
        ]);
        ReviewedMap = reviewed;
        builder.Services.AddSingleton(Clock);
        builder.Services.AddSingleton(new HotelPolicy());
        builder.Services.AddSingleton(reviewed);
        builder.Services.AddSingleton(new HotelSnapshotCache(Clock));
        builder.Services.AddSingleton<IHotelCapacity>(capacity);
        builder.Services.AddSingleton<IHotelReservationStore>(Store);
        builder.Services.AddSingleton<IHotelPrivateProtector>(new OwnedHotelMemoryProtector());
        builder.Services.AddSingleton<IHotelSearchProvider>(du);
        builder.Services.AddSingleton<IHotelSearchProvider>(hb);
        builder.Services.AddSingleton<IHotelBookingProvider>(du);
        builder.Services.AddSingleton<IHotelBookingProvider>(hb);
        builder.Services.AddScoped<HotelSearchService>();
        builder.Services.AddScoped<HotelReservationService>();
        builder.Services.AddScoped<HotelObservationRunner>();
        builder.Services.Configure<HotelRequestOptions>(_ => { });
        app = builder.Build();
        app.UseMiddleware<HotelsRequestPolicyMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        Delegate searchRoute =
            (Func<HotelSearchDto, HotelSearchService, CancellationToken, Task<IResult>>)
                HotelEndpoints.Search;
        Delegate ratesRoute =
            (Func<HotelRatesDto, HotelSearchService, CancellationToken, Task<IResult>>)
                HotelEndpoints.Rates;
        Delegate quoteRoute =
            (Func<
                Guid,
                HotelQuoteDto,
                HttpContext,
                HotelSearchService,
                HotelReservationService,
                CancellationToken,
                Task<IResult>
            >)
                HotelEndpoints.Quote;
        Delegate purchaseRoute =
            (Func<
                Guid,
                HotelPurchase,
                HttpContext,
                HotelReservationService,
                CancellationToken,
                Task<IResult>
            >)
                HotelEndpoints.Purchase;
        Delegate detailRoute =
            (Func<Guid, HttpContext, HotelReservationService, CancellationToken, Task<IResult>>)
                HotelEndpoints.Detail;
        Delegate listRoute =
            (Func<HttpContext, HotelReservationService, CancellationToken, Task<IResult>>)
                HotelEndpoints.List;
        app.MapPost("/api/hotels/search", searchRoute);
        app.MapPost("/api/hotels/rates", ratesRoute);
        app.MapPut("/api/hotels/reservations/{id:guid}/quote", quoteRoute)
            .RequireAuthorization("hotels:book");
        app.MapPost("/api/hotels/reservations/{id:guid}/purchase", purchaseRoute)
            .RequireAuthorization("hotels:book");
        app.MapGet("/api/hotels/reservations/{id:guid}", detailRoute)
            .RequireAuthorization("hotels:book");
        app.MapGet("/api/hotels/reservations", listRoute).RequireAuthorization("hotels:book");
        app.MapGet(
            "/__owned-fixture/stats",
            () =>
                Results.Json(
                    new
                    {
                        fictional = true,
                        database = false,
                        duffelCreates = duffel.Creates,
                        hotelbedsCreates = hotelbeds.Creates,
                        duffelReads = duffel.Reads,
                        hotelbedsReads = hotelbeds.Reads,
                        reservations = Store.Count,
                    }
                )
        );
        app.MapPost(
            "/__owned-fixture/scenario",
            (JsonElement body) =>
            {
                var next = body.GetProperty("next").GetString();
                duffel.Unknown = hotelbeds.Unknown = next == "unknown";
                hotelbeds.ValidationFailure = next == "validation-400";
                return Results.Ok();
            }
        );
    }

    public static async Task<HotelM2MemoryFixture> StartAsync(
        CancellationToken ct,
        int? port = null,
        TimeProvider? time = null
    )
    {
        var fixture = new HotelM2MemoryFixture(port, time);
        await fixture.app.StartAsync(ct);
        fixture.Client = port is null
            ? fixture.app.GetTestClient()
            : new() { BaseAddress = new("http://127.0.0.1:" + port.Value) };
        return fixture;
    }

    public Task WaitForShutdownAsync(CancellationToken ct) => app.WaitForShutdownAsync(ct);

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await app.DisposeAsync();
    }

    public sealed class OwnedDuffelWire : HttpMessageHandler, IDuffelStaysFictionalTransport
    {
        public TimeProvider Clock { get; set; } = TimeProvider.System;
        private readonly Dictionary<string, JsonNode> rates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonNode> quotes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonNode> bookings = new(StringComparer.Ordinal);
        private int sequence;
        public int Creates { get; private set; }
        public int Reads { get; private set; }
        public int Preparations { get; private set; }
        public bool Unknown { get; set; }
        public bool ReservedWithoutDetails { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message,
            CancellationToken ct
        )
        {
            var path = message.RequestUri!.AbsolutePath;
            var ordinal = Interlocked.Increment(ref sequence);
            if (rates.Count >= 128 || quotes.Count >= 128 || bookings.Count >= 64)
                return Reply(new JsonObject(), HttpStatusCode.TooManyRequests);
            var body = message.Content is null
                ? null
                : JsonNode.Parse(await message.Content.ReadAsStringAsync(ct))!["data"];
            if (path == "/stays/search")
            {
                var reply = Template("search");
                var data = reply["data"]!["results"]![0]!;
                data["id"] = "srr_owned_" + ordinal;
                data["check_in_date"] = body!["check_in_date"]!.DeepClone();
                data["check_out_date"] = body["check_out_date"]!.DeepClone();
                data["guests"] = body["guests"]!.DeepClone();
                data["expires_at"] = Clock.GetUtcNow().AddMinutes(5);
                data["cheapest_rate_total_amount"] = "200.00";
                UpdateProperty(data["accommodation"]!);
                var rate = data["accommodation"]!["rooms"]![0]!["rates"]![0]!;
                rate["id"] = "rat_owned_" + ordinal;
                rate["total_amount"] = "200.00";
                rate["expires_at"] = Clock.GetUtcNow().AddMinutes(5);
                rates[data["id"]!.GetValue<string>()] = data.DeepClone();
                rates[rate["id"]!.GetValue<string>()] = data.DeepClone();
                return Reply(reply);
            }
            if (path.Contains("fetch_all_rates", StringComparison.Ordinal))
                return Reply(new JsonObject { ["data"] = rates[path.Split('/')[3]].DeepClone() });
            if (path == "/stays/quotes")
            {
                Preparations++;
                var source = rates[body!["rate_id"]!.GetValue<string>()];
                var reply = Template("quote");
                var data = reply["data"]!;
                data["id"] = "quo_owned_" + ordinal;
                foreach (
                    var field in new[]
                    {
                        "check_in_date",
                        "check_out_date",
                        "guests",
                        "accommodation",
                    }
                )
                    data[field] = source[field]!.DeepClone();
                data["total_amount"] = "200.00";
                quotes[data["id"]!.GetValue<string>()] = data.DeepClone();
                return Reply(reply);
            }
            if (message.Method == HttpMethod.Get)
            {
                Reads++;
                return bookings.TryGetValue(path.Split('/')[3], out var saved)
                    ? Reply(new JsonObject { ["data"] = saved.DeepClone() })
                    : Reply(new JsonObject(), HttpStatusCode.NotFound);
            }
            Creates++;
            if (Unknown)
                return Reply(new JsonObject(), HttpStatusCode.GatewayTimeout);
            var quote = quotes[body!["quote_id"]!.GetValue<string>()];
            var booking = Template("booking201")["data"]!;
            booking["id"] = "bok_owned_" + ordinal;
            foreach (var field in new[] { "check_in_date", "check_out_date", "accommodation" })
                booking[field] = quote[field]!.DeepClone();
            booking["guest_types"] = quote["guests"]!.DeepClone();
            bookings[booking["id"]!.GetValue<string>()] = booking.DeepClone();
            if (ReservedWithoutDetails)
                booking["guest_types"] = null;
            return Reply(new JsonObject { ["data"] = booking.DeepClone() }, HttpStatusCode.Created);
        }

        private static void UpdateProperty(JsonNode property)
        {
            property["name"] = "Owned Fictional House";
            property["location"] = new JsonObject
            {
                ["address"] = new JsonObject { ["line_one"] = "1 Owned Fictional Square" },
            };
        }

        private static JsonNode Template(string name) =>
            JsonNode.Parse(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        File.Exists(
                            Path.Combine(AppContext.BaseDirectory, "HotelsFixtures", name + ".json")
                        )
                            ? "HotelsFixtures"
                            : "Fixtures",
                        name + ".json"
                    )
                )
            )!;
    }

    public sealed class OwnedHotelbedsWire : HttpMessageHandler, IHotelbedsFictionalTransport
    {
        private sealed record Scope(string CheckIn, string CheckOut, int Adults);

        private readonly Dictionary<string, Scope> rates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonNode> bookings = new(StringComparer.Ordinal);
        private int sequence;
        public int Creates { get; private set; }
        public int Reads { get; private set; }
        public int Preparations { get; private set; }
        public bool Unknown { get; set; }
        public bool ValidationFailure { get; set; }
        public bool ReservedWithoutDetails { get; set; }
        public bool ReadsUnavailable { get; set; }
        public decimal? ReadTotal { get; set; }
        public Func<Task>? AfterEffect { get; set; }

        public string ExportRateScopes() => JsonSerializer.Serialize(rates);

        public void ImportRateScopes(string json)
        {
            foreach (var item in JsonSerializer.Deserialize<Dictionary<string, Scope>>(json)!)
                rates[item.Key] = item.Value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage message,
            CancellationToken ct
        )
        {
            var path = message.RequestUri!.AbsolutePath;
            var ordinal = Interlocked.Increment(ref sequence);
            if (rates.Count >= 128 || bookings.Count >= 64)
                return Reply(new JsonObject(), HttpStatusCode.TooManyRequests);
            if (message.Method == HttpMethod.Get)
            {
                Reads++;
                if (ReadsUnavailable)
                    return Reply(new JsonObject(), HttpStatusCode.BadGateway);
                if (!bookings.TryGetValue(path.Split('/')[4], out var saved))
                    return Reply(new JsonObject(), HttpStatusCode.NotFound);
                var observed = saved.DeepClone();
                if (ReadTotal is { } amount)
                    observed["totalNet"] = amount;
                return Reply(new JsonObject { ["booking"] = observed });
            }
            var body = JsonNode.Parse(await message.Content!.ReadAsStringAsync(ct))!;
            if (path.EndsWith("/hotels", StringComparison.Ordinal))
            {
                var scope = new Scope(
                    body["stay"]!["checkIn"]!.GetValue<string>(),
                    body["stay"]!["checkOut"]!.GetValue<string>(),
                    body["occupancies"]![0]!["adults"]!.GetValue<int>()
                );
                var key = "owned-hbx-" + ordinal;
                rates[key] = scope;
                return Reply(
                    new JsonObject
                    {
                        ["hotels"] = new JsonObject
                        {
                            ["checkIn"] = scope.CheckIn,
                            ["checkOut"] = scope.CheckOut,
                            ["hotels"] = new JsonArray(Hotel(scope, key, "RECHECK")),
                        },
                    }
                );
            }
            var previous = body["rooms"]![0]!["rateKey"]!.GetValue<string>();
            var selected = rates[previous];
            var stageKey = "owned-hbx-stage-" + ordinal;
            rates[stageKey] = selected;
            if (path.EndsWith("/checkrates", StringComparison.Ordinal))
            {
                Preparations++;
                return Reply(new JsonObject { ["hotel"] = Hotel(selected, stageKey, "BOOKABLE") });
            }
            Creates++;
            if (ValidationFailure)
                return Reply(
                    new JsonObject
                    {
                        ["error"] = new JsonObject
                        {
                            ["code"] = "INVALID_DATA-11111111-1111-4111-8111-111111111111",
                            ["message"] = "Owned fictional validation refusal",
                        },
                    },
                    HttpStatusCode.BadRequest
                );
            if (Unknown)
                return Reply(new JsonObject(), HttpStatusCode.GatewayTimeout);
            var reference = "OWN-" + ordinal;
            var booking = new JsonObject
            {
                ["reference"] = reference,
                ["clientReference"] = body["clientReference"]!.DeepClone(),
                ["status"] = "CONFIRMED",
                ["totalNet"] = 190m,
                ["currency"] = "GBP",
                ["hotel"] = Hotel(selected, stageKey, "BOOKABLE"),
            };
            bookings[reference] = booking.DeepClone();
            if (AfterEffect is not null)
                await AfterEffect();
            if (ReservedWithoutDetails)
                booking.Remove("hotel");
            return Reply(new JsonObject { ["booking"] = booking });
        }

        private static JsonObject Hotel(Scope scope, string key, string rateType) =>
            new()
            {
                ["code"] = 900001,
                ["name"] = "Owned Fictional House",
                ["checkIn"] = scope.CheckIn,
                ["checkOut"] = scope.CheckOut,
                ["currency"] = "GBP",
                ["rooms"] = new JsonArray(
                    new JsonObject
                    {
                        ["code"] = "DBL.OWN",
                        ["name"] = "Owned double",
                        ["rates"] = new JsonArray(
                            new JsonObject
                            {
                                ["rateKey"] = key,
                                ["rateType"] = rateType,
                                ["rateClass"] = "NOR",
                                ["net"] = "190.00",
                                ["paymentType"] = "AT_WEB",
                                ["packaging"] = false,
                                ["boardCode"] = "RO",
                                ["boardName"] = "ROOM ONLY",
                                ["rooms"] = 1,
                                ["adults"] = scope.Adults,
                                ["children"] = 0,
                                ["cancellationPolicies"] = new JsonArray(
                                    new JsonObject
                                    {
                                        ["amount"] = "190.00",
                                        ["from"] = scope.CheckIn + "T12:00:00+01:00",
                                    }
                                ),
                                ["taxes"] = new JsonObject
                                {
                                    ["allIncluded"] = true,
                                    ["taxes"] = new JsonArray(),
                                },
                            }
                        ),
                    }
                ),
            };
    }

    private static HttpResponseMessage Reply(
        JsonNode json,
        HttpStatusCode status = HttpStatusCode.OK
    ) =>
        new(status)
        {
            Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"),
        };
}

public sealed class OwnedHotelMemoryStore : IHotelReservationStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, HotelReservation> rows = [];
    private int admissionContenders;
    private TaskCompletionSource admissionGate = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public void CoordinateNextAdmissionLoads(int contenders)
    {
        if (contenders is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(contenders));
        lock (gate)
        {
            admissionContenders = contenders;
            admissionGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
                return rows.Count;
        }
    }

    public HotelReservation? Snapshot(Guid id)
    {
        lock (gate)
            return rows.TryGetValue(id, out var row) ? Clone(row) : null;
    }

    public async Task<ErrorOr<HotelReservation?>> LoadAsync(
        Guid id,
        Guid? owner,
        CancellationToken ct
    )
    {
        var row = Snapshot(id);
        Task? wait = null;
        lock (gate)
        {
            if (
                owner is not null
                && row is { AttemptId: null, ProtectedContext: not null }
                && admissionContenders > 0
            )
            {
                admissionContenders--;
                wait = admissionGate.Task;
                if (admissionContenders == 0)
                    admissionGate.TrySetResult();
            }
        }
        if (wait is not null)
            await wait.WaitAsync(ct);
        return row is not null && (owner is null || row.OwnerId == owner) ? row : null;
    }

    public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct)
    {
        lock (gate)
            return Task.FromResult<ErrorOr<HotelReservation[]>>(
                rows.Values.Where(r => r.OwnerId == owner).Select(Clone).ToArray()
            );
    }

    public Task<ErrorOr<bool>> SaveAsync(
        HotelReservation row,
        int expectedVersion,
        HotelWork[] work,
        CancellationToken ct
    )
    {
        lock (gate)
        {
            var current = rows.GetValueOrDefault(row.Id);
            if ((current?.Version ?? 0) != expectedVersion)
                return Task.FromResult<ErrorOr<bool>>(false);
            if (current is null && rows.Count >= 64)
                return Task.FromResult<ErrorOr<bool>>(HotelErrors.Capacity);
            if (
                current?.AttemptId is not null
                && (
                    current.AttemptId != row.AttemptId
                    || current.Provider != row.Provider
                    || current.QuoteRevision != row.QuoteRevision
                )
            )
                return Task.FromResult<ErrorOr<bool>>(HotelErrors.Conflict);
            row.Version = expectedVersion + 1;
            rows[row.Id] = Clone(row);
            return Task.FromResult<ErrorOr<bool>>(true);
        }
    }

    private static HotelReservation Clone(HotelReservation row) =>
        JsonSerializer.Deserialize<HotelReservation>(JsonSerializer.Serialize(row))!;
}

internal sealed class OwnedHotelMemoryProtector : IHotelPrivateProtector
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);

    public ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag, JsonSerializer.SerializeToUtf8Bytes(binding));
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding)
    {
        try
        {
            var data = Convert.FromBase64String(envelope);
            var plaintext = new byte[data.Length - 28];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                data.AsSpan(0, 12),
                data.AsSpan(28),
                data.AsSpan(12, 16),
                plaintext,
                JsonSerializer.SerializeToUtf8Bytes(binding)
            );
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
            when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return HotelErrors.Unavailable;
        }
    }
}

internal sealed class OwnedHotelAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Headers.Authorization.ToString();
        if (
            token
            is not (
                "Bearer fictional-hotels-owner"
                or "Bearer fictional-hotels-foreign"
                or "Bearer fictional-hotels-no-scope"
            )
        )
            return Task.FromResult(AuthenticateResult.NoResult());
        var owner =
            token == "Bearer fictional-hotels-foreign"
                ? "aaaaaaaa-1111-4111-8111-111111111111"
                : HotelM2MemoryFixture.Owner;
        var scope = token == "Bearer fictional-hotels-no-scope" ? "" : "hotels:book";
        return Task.FromResult(
            AuthenticateResult.Success(
                new(
                    new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new(ClaimTypes.NameIdentifier, owner),
                                new("scope", scope),
                                new("aud", "travel-web"),
                            ],
                            Scheme.Name
                        )
                    ),
                    Scheme.Name
                )
            )
        );
    }
}
