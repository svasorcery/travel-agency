using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed record HotelM2SeedStock(Guid Id, string Provider, string? QuoteId, string BookingId);

public sealed record HotelM2SeedTemplate(
    StayTerms Terms,
    ProviderContextHandle Context,
    HotelAdmissionBinding Binding
);

public sealed record HotelM2SeedResult(
    HotelM2SeedRow[] Rows,
    HotelM2SeedStock[] StatusStock,
    HotelM2SeedStock[] ObservationStock,
    HotelWireSnapshot[] Sources
)
{
    [JsonIgnore]
    public IReadOnlyDictionary<string, HotelM2SeedTemplate> Templates { get; init; } =
        new Dictionary<string, HotelM2SeedTemplate>();
}

public static class HotelM2LoadSeed
{
    public static Task<HotelM2SeedResult> PrepareMemoryAsync(
        HotelM2MemoryFixture fixture,
        HotelM2LoadProfile profile,
        CancellationToken ct
    ) =>
        PrepareAsync(
            profile,
            fixture.Services,
            fixture.DuffelWire,
            fixture.HotelbedsWire,
            fixture.LoadControl!,
            ct
        );

    public static async Task<HotelM2SeedResult> PrepareAsync(
        HotelM2LoadProfile profile,
        IServiceProvider services,
        HotelM2MemoryFixture.OwnedDuffelWire duffel,
        HotelM2MemoryFixture.OwnedHotelbedsWire hotelbeds,
        HotelM2LoadControl control,
        CancellationToken ct
    )
    {
        if (control.Active)
            throw new InvalidOperationException("Seed cannot run after timed admission.");
        using (var check = services.CreateScope())
        {
            var existing = await check
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .ListAsync(Guid.Parse(HotelM2MemoryFixture.Owner), ct);
            if (existing.IsError || existing.Value.Length != 0)
                throw new InvalidOperationException("Seed requires an empty owned store.");
        }
        using var d = new HttpMessageInvoker(duffel, disposeHandler: false);
        using var h = new HttpMessageInvoker(hotelbeds, disposeHandler: false);
        var clock = services.GetRequiredService<TimeProvider>();
        var providers = services.GetServices<IHotelBookingProvider>().ToArray();
        var map = services
            .GetRequiredService<HotelReviewedMap>()
            .Pairs.Single(p => p.Id == $"load-{profile.Code}-001");
        var stock = new List<HotelM2SeedStock>();
        var templates = new Dictionary<string, HotelM2SeedTemplate>(StringComparer.Ordinal);
        foreach (var source in new[] { "duffel-stays", "hotelbeds" })
        {
            var provider = providers.Single(p => p.Key == source);
            var descriptor = ((IHotelSourceDescriptor)provider).CurrentSource;
            var raw = source == "duffel-stays" ? d : h;
            var availability = await Send(
                raw,
                source == "duffel-stays" ? "/stays/search" : "/hotel-api/1.0/hotels",
                source == "duffel-stays"
                    ? (object)
                        new
                        {
                            data = new
                            {
                                rooms = 1,
                                check_in_date = HotelM2LoadProfile.Date(profile.CheckIn),
                                check_out_date = HotelM2LoadProfile.Date(profile.CheckOut),
                                guests = Enumerable
                                    .Range(0, profile.Adults)
                                    .Select(_ => new { type = "adult" })
                                    .ToArray(),
                            },
                        }
                    : new
                    {
                        stay = new
                        {
                            checkIn = HotelM2LoadProfile.Date(profile.CheckIn),
                            checkOut = HotelM2LoadProfile.Date(profile.CheckOut),
                        },
                        occupancies = new[]
                        {
                            new
                            {
                                rooms = 1,
                                adults = profile.Adults,
                                children = 0,
                            },
                        },
                        hotels = new { hotel = profile.HotelbedsIds },
                    },
                ct
            );
            var rate =
                source == "duffel-stays"
                    ? availability["data"]!["results"]![0]!["accommodation"]!["rooms"]![0]![
                        "rates"
                    ]![0]!["id"]!.GetValue<string>()
                    : availability["hotels"]!["hotels"]![0]!["rooms"]![0]!["rates"]![0]![
                        "rateKey"
                    ]!.GetValue<string>();
            StayTerms? accepted = null;
            var sourceRows = control
                .Schedule.Seeds.Where(r => r.Provider == source)
                .OrderBy(r =>
                    r.Role == "confirmed" ? 0
                    : r.Role == "draft" ? 1
                    : 2
                )
                .ThenBy(r => r.Index)
                .ToArray();
            foreach (var seed in sourceRows)
            {
                var prepared = await Send(
                    raw,
                    source == "duffel-stays" ? "/stays/quotes" : "/hotel-api/1.0/checkrates",
                    source == "duffel-stays"
                        ? (object)new { data = new { rate_id = rate } }
                        : new { rooms = new[] { new { rateKey = rate } } },
                    ct
                );
                string? quote = null;
                ProviderContextHandle context;
                if (source == "duffel-stays")
                {
                    quote = prepared["data"]!["id"]!.GetValue<string>();
                    context = new(
                        1,
                        JsonSerializer.Serialize(new { QuoteId = quote, RateId = rate })
                    );
                }
                else
                {
                    var preparedKey = prepared["hotel"]!["rooms"]![0]!["rates"]![0]![
                        "rateKey"
                    ]!.GetValue<string>();
                    context = new(
                        2,
                        JsonSerializer.Serialize(
                            new
                            {
                                source = descriptor.Identity,
                                rateKey = preparedKey,
                                credentialIdentity = descriptor.TransportCredentialFingerprint,
                            },
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)
                        )
                    );
                }
                var now = clock.GetUtcNow();
                var attempt =
                    seed.Role == "draft"
                        ? (Guid?)null
                        : profile.Id(source + ":attempt:" + seed.Role, seed.Index);
                string? bookingId = null;
                if (attempt is { } admitted)
                {
                    var created = await Send(
                        raw,
                        source == "duffel-stays" ? "/stays/bookings" : "/hotel-api/1.0/bookings",
                        source == "duffel-stays"
                            ? (object)
                                new
                                {
                                    data = new
                                    {
                                        quote_id = quote,
                                        metadata = new { travel_attempt = admitted.ToString("N") },
                                        guests = Enumerable
                                            .Range(1, profile.Adults)
                                            .Select(i => new
                                            {
                                                given_name = "Fictional",
                                                family_name = "Traveller" + i,
                                            })
                                            .ToArray(),
                                    },
                                }
                            : new
                            {
                                clientReference = admitted.ToString("N")[..20],
                                holder = new { name = "Fictional", surname = "Traveller" },
                                rooms = new[]
                                {
                                    new
                                    {
                                        rateKey = JsonNode.Parse(context.Value)![
                                            "rateKey"
                                        ]!.GetValue<string>(),
                                    },
                                },
                            },
                        ct
                    );
                    bookingId =
                        source == "duffel-stays"
                            ? created["data"]!["id"]!.GetValue<string>()
                            : created["booking"]!["reference"]!.GetValue<string>();
                    if (seed.Role == "confirmed")
                    {
                        // Like the M1 fixture, normalize a known seeded booking directly through
                        // the real reader. ObservationRunner owns runtime recovery permits; this
                        // setup read never opens/resets the application's admission window.
                        var observed = await ((IHotelReservationReader)provider).ReadAsync(
                            bookingId,
                            ct
                        );
                        if (
                            observed.Kind != ObservationKind.Confirmed
                            || observed.Facts?.Actual is null
                        )
                            throw new InvalidOperationException("Seeded source facts unavailable.");
                        accepted = observed.Facts.Actual;
                    }
                }
                if (accepted is null)
                    throw new InvalidOperationException("Confirmed seed must precede drafts.");
                var binding = new HotelAdmissionBinding(
                    2,
                    descriptor.Identity,
                    descriptor.ConfigurationRevision,
                    descriptor.PricingProfile,
                    descriptor.ApprovalRevision,
                    map.Id,
                    map.Revision,
                    now.AddMinutes(5),
                    descriptor.TransportAccountBinding,
                    HotelPropertyMatching.MappingDigest(map),
                    descriptor.TransportCredentialFingerprint
                );
                var row = new HotelReservation
                {
                    Id = seed.Id,
                    OwnerId = Guid.Parse(HotelM2MemoryFixture.Owner),
                    QuoteRevision = 1,
                    Provider = source,
                    Environment = "Test",
                    ProviderAccount = descriptor.TransportAccountBinding!,
                    Terms = accepted,
                    SourcePreparationReference = quote,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ValidUntil = now.AddMinutes(5),
                    PrivatePurgeAt = now.AddHours(24),
                    State = new ReservationState
                    {
                        QuoteContractVersion = 2,
                        AdmissionBinding = binding,
                        AcceptedPrice = new(
                            "SupplierCost",
                            accepted.Total,
                            null,
                            "WholeStay",
                            source,
                            [],
                            accepted
                                .MandatoryLocalPayments?.Select(m => new HotelObligation(
                                    "Excluded",
                                    m,
                                    "WholeStay"
                                ))
                                .ToArray()
                        ),
                        AcceptedConditions = new(accepted.Cancellation.Kind, [], [], source),
                    },
                };
                using var scope = services.CreateScope();
                var protector = scope.ServiceProvider.GetRequiredService<IHotelPrivateProtector>();
                var protectedContext = protector.Protect(
                    JsonSerializer.Serialize(context),
                    HotelReservationService.Binding(row, "context")
                );
                if (protectedContext.IsError)
                    throw new InvalidOperationException("Seed context protection failed.");
                row.ProtectedContext = protectedContext.Value;
                HotelWork[] work = [];
                if (attempt is { } spent)
                {
                    row.AttemptId = spent;
                    row.ActionId = profile.Id(source + ":action:" + seed.Role, seed.Index);
                    row.AdmittedAt = now;
                    row.SenderFinished = true;
                    row.PrivatePurgeAt = null;
                    row.State.Outcome = ReservationOutcome.Unknown;
                    row.State.Review.Add(ReviewReason.TransportUnknown);
                    row.State.EscalateAt = now.AddMinutes(10);
                    var party = protector.Protect(
                        JsonSerializer.Serialize(Party(profile.Adults)),
                        HotelReservationService.Binding(row, "party")
                    );
                    if (party.IsError)
                        throw new InvalidOperationException("Seed party protection failed.");
                    row.ProtectedParty = party.Value;
                    work = [new(seed.Id, true, row.State.EscalateAt.Value)];
                    if (seed.Role == "confirmed")
                        row.State.Apply(
                            new(ObservationKind.Confirmed, new(bookingId!, null, accepted)),
                            accepted,
                            now
                        );
                    else
                    {
                        var callback = control.Schedule.Jobs.SingleOrDefault(j =>
                            j.Kind == "callback" && j.ReservationId == seed.Id
                        );
                        if (callback is not null)
                            control.RegisterBooking(source, bookingId!, callback.Id);
                    }
                    stock.Add(new(seed.Id, source, quote, bookingId!));
                }
                var saved = await scope
                    .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                    .SaveAsync(row, 0, work, ct);
                if (saved.IsError || !saved.Value)
                    throw new InvalidOperationException("Fixed M2 seed failed.");
                templates.TryAdd(source, new(accepted, context, binding));
            }
        }
        if (
            services
                .GetRequiredService<HotelCapacity>()
                .GetSourceSnapshots()
                .Any(s => s.Capacity.TotalStarts != 0 || s.Capacity.WindowUntil != default)
        )
            throw new InvalidOperationException(
                "Setup spent runtime admission; no reset is allowed."
            );
        var sources = new[]
        {
            duffel.CaptureMeasurements(true),
            hotelbeds.CaptureMeasurements(true),
        };
        if (
            sources.Single(s => s.Provider == "duffel-stays").Starts != 40
            || sources.Single(s => s.Provider == "hotelbeds").Starts != 16
        )
            throw new InvalidOperationException("Fixed seed source-step denominator changed.");
        var roles = control.Schedule.Seeds.ToDictionary(s => s.Id, s => s.Role);
        return new(
            control.Schedule.Seeds.ToArray(),
            stock.Where(s => roles[s.Id] == "confirmed").ToArray(),
            stock.Where(s => roles[s.Id] == "observation").ToArray(),
            sources
        )
        {
            Templates = templates,
        };
    }

    public static HotelParty Party(int adults) =>
        new(
            Enumerable
                .Range(1, adults)
                .Select(i => new HotelGuest("Fictional", "Traveller" + i))
                .ToArray(),
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );

    private static async Task<JsonNode> Send(
        HttpMessageInvoker owned,
        string path,
        object body,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://owned.invalid" + path)
        {
            Content = JsonContent.Create(body),
        };
        using var response = await owned.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
    }
}
