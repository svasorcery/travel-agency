using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Shared.Abstractions;

namespace Travel.Trips.LocalStack;

[TestOnly]
internal static class HotelLinksSeed
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<IDuffelStaysFictionalTransport>(_ => new NoSupplier());
        foreach (var name in new[] { "hotels-duffel", "hotels-hotelbeds" })
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new NoSupplier());
    }

    internal static async Task SeedAsync(
        IServiceProvider services,
        LocalStackState state,
        Guid owner,
        CancellationToken ct
    )
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var store = provider.GetRequiredService<IHotelReservationStore>();
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        var id = new Guid(
            SHA256.HashData(Encoding.UTF8.GetBytes(state.Id + "/hotel-reservation")).AsSpan(0, 16)
        );
        StayTerms accepted = new(
            new("fictional-hotel", "Fictional quoted Petersburg stay", null, null, null),
            "Double",
            "fictional-rate",
            new(new(2027, 6, 13), new(2027, 6, 15)),
            new(1),
            new(4500m, "RUB"),
            [],
            [],
            new("NonRefundable", []),
            [],
            true,
            "ImmediateTest"
        );
        var reservation = new HotelReservation
        {
            Id = id,
            OwnerId = owner,
            QuoteRevision = 1,
            Provider = "duffel",
            Terms = accepted,
            ValidUntil = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now,
            PrivatePurgeAt = now.AddDays(90),
            State = new()
            {
                Outcome = ReservationOutcome.Confirmed,
                EverReserved = true,
                Facts = new(
                    "fictional-hotel-reference",
                    null,
                    accepted with
                    {
                        Property = accepted.Property with
                        {
                            Name = "Fictional White Nights Guesthouse",
                        },
                    }
                ),
            },
        };
        var context = provider
            .GetRequiredService<IHotelPrivateProtector>()
            .Protect(
                JsonSerializer.Serialize(
                    new { QuoteId = "fictional-quote", RateId = "fictional-rate" }
                ),
                HotelReservationService.Binding(reservation, "context")
            );
        if (context.IsError)
            throw new LocalStackRefusal("The fictional Hotels context could not be protected.");
        reservation.ProtectedContext = context.Value;
        var saved = await store.SaveAsync(reservation, 0, [], ct);
        if (saved.IsError || !saved.Value)
            throw new LocalStackRefusal("The fictional Hotels order fixture could not be saved.");
    }

    private sealed class NoSupplier : HttpMessageHandler, IDuffelStaysFictionalTransport
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException(
                "Hotel link fixtures do not use supplier transport."
            );
    }
}
