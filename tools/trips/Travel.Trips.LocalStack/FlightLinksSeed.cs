using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;
using Wolverine.Marten;

namespace Travel.Trips.LocalStack;

// Local CLI and test Host share a same-user pipe in the host container. Only two fixed
// fixture operations cross it; database writes use the running Host's real enrolled outbox.
[TestOnly]
public static class FlightLinksSeed
{
    private const byte Seed = 1;
    private const byte Close = 2;
    private const int HeldVersion = 6;

    internal static void RequireProfile(LocalStackState state)
    {
        E2eHostFactory.ValidateMode(
            state.Mode,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? ""
        );
        if (state.E2eProfile != "flight-links")
            throw new LocalStackRefusal("Flight fixtures require the owned flight-links profile.");
    }

    private static string PipeName(LocalStackState state) => "travel-trips-flights-" + state.Id;

    internal static async Task RequestAsync(LocalStackState state, bool close, CancellationToken ct)
    {
        RequireProfile(state);
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName(state),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        );
        await pipe.ConnectAsync(5000, ct);
        await pipe.WriteAsync(new byte[] { close ? Close : Seed }, ct);
        var reply = new byte[1];
        await pipe.ReadExactlyAsync(reply, ct);
        if (reply[0] != 1)
            throw new LocalStackRefusal(
                reply[0] switch
                {
                    2 => "The owned Host has not passed readiness.",
                    3 =>
                        "The owned flight fixture rejected the profile, ownership or source state.",
                    4 =>
                        "The flight fixture exceeded its budget while writing or waiting for projection.",
                    _ => "The flight fixture failed while accessing its local dependencies.",
                }
            );
    }

    public static async Task ServeAsync(
        LocalStackState state,
        IServiceProvider services,
        CancellationToken stopping
    )
    {
        RequireProfile(state);
        while (!stopping.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName(state),
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            );
            await pipe.WaitForConnectionAsync(stopping);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var ct = timeout.Token;
            var reply = new byte[1];
            try
            {
                var request = new byte[1];
                await pipe.ReadExactlyAsync(request, ct);
                if (request[0] is not (Seed or Close))
                    throw new LocalStackRefusal("Unknown flight fixture operation.");
                reply[0] = await ExecuteAsync(state, services, request[0], ct);
            }
            catch (LocalStackRefusal)
            {
                reply[0] = 3;
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                reply[0] = 4;
            }
            catch (Exception) when (!stopping.IsCancellationRequested)
            {
                // No private error text crosses this channel. A refused or uncertain operation is never replayed here.
            }
            try
            {
                await pipe.WriteAsync(reply, stopping);
            }
            catch (IOException)
            { /* A disconnected caller cannot trigger another mutation. */
            }
        }
    }

    private static async Task<byte> ExecuteAsync(
        LocalStackState state,
        IServiceProvider services,
        byte request,
        CancellationToken ct
    )
    {
        var current = ReadyState(
            (await ToolProgram.LocalProbeAsync(ct)).Ready,
            state,
            LocalStackState.ReadContainer
        );
        if (current is null)
            return 2;
        await using var owned = await LocalDatabase.OpenOwnedAsync(current, ct);
        var owners = ReadOwners(current);
        await ApplyAsync(services, current, owners[0], false, request == Close, ct);
        if (request == Seed)
            await ApplyAsync(services, current, owners[1], true, false, ct);
        return 1;
    }

    internal static LocalStackState? ReadyState(
        bool ready,
        LocalStackState running,
        Func<string, LocalStackState> readMountedState
    )
    {
        if (!ready)
            return null;
        var current = readMountedState(running.StateDir);
        if (running.E2eProfile != "flight-links" || running.Mode != "e2e" || current != running)
            throw new LocalStackRefusal(
                "The mounted state no longer belongs to the running flight-links Host."
            );
        return current;
    }

    private static Guid[] ReadOwners(LocalStackState state)
    {
        var path = Path.Combine(state.StateDir, "private", "credentials.json");
        LocalStackState.RequireNoLinks(path);
        if (new FileInfo(path).Length is <= 0 or > 4096)
            throw new LocalStackRefusal("Invalid fixture owner file.");
        var credentials = JsonSerializer.Deserialize<FictionalCredential[]>(
            File.ReadAllBytes(path),
            LocalStackJson.Options
        );
        if (
            credentials is not { Length: 2 }
            || credentials[0].Username != "trips-a-" + state.Id[..8]
            || credentials[1].Username != "trips-b-" + state.Id[..8]
            || credentials.Any(c => c.Subject == Guid.Empty)
            || credentials[0].Subject == credentials[1].Subject
        )
            throw new LocalStackRefusal("The provisioned fixture owners are missing.");
        return credentials.Select(c => c.Subject).ToArray();
    }

    private static async Task ApplyAsync(
        IServiceProvider services,
        LocalStackState state,
        Guid owner,
        bool reverse,
        bool close,
        CancellationToken ct
    )
    {
        var label = reverse ? "B" : "A";
        var orderId = new Guid(
            SHA256
                .HashData(Encoding.UTF8.GetBytes(state.Id + "/flight-order/" + label))
                .AsSpan(0, 16)
        );
        var providerOrder = "ord_fictional_" + label;
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        var session = provider.GetRequiredService<IDocumentSession>();
        var stream = await session.Events.FetchForWriting<BookingAggregate>(orderId, ct);
        var aggregate = stream.Aggregate;
        var version = stream.CurrentVersion;
        var status = aggregate?.Status;
        var append = RequiresAppend(
            aggregate,
            version,
            owner,
            orderId,
            providerOrder,
            reverse,
            close
        );
        if (append && !close)
        {
            var events = HeldEvents(
                orderId,
                owner,
                providerOrder,
                reverse,
                now,
                provider.GetRequiredService<IBookingPassengerPartyProtector>()
            );
            session.Events.StartStream<BookingAggregate>(orderId, events);
            await session.SaveBookingWithReconcileAsync(
                provider.GetRequiredService<IMartenOutbox>(),
                orderId,
                [],
                ct
            );
            version = events.Length;
            status = BookingStatus.Held;
        }
        else if (append)
        {
            stream.AppendOne(new OrderCancelled(CancelReason.Airline, now));
            await session.SaveBookingWithReconcileAsync(
                provider.GetRequiredService<IMartenOutbox>(),
                orderId,
                [],
                ct
            );
            version = HeldVersion + 1;
            status = BookingStatus.Cancelled;
        }
        var query = provider.GetRequiredService<IOrderReadModelQueries>();
        while (!ct.IsCancellationRequested)
        {
            var row = await query.GetAsync(orderId, owner, ct);
            if (row?.ProjectedStreamVersion == version && row?.Status == status.ToString())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
        ct.ThrowIfCancellationRequested();
    }

    internal static bool RequiresAppend(
        BookingAggregate? booking,
        long? version,
        Guid owner,
        Guid orderId,
        string providerOrder,
        bool reverse,
        bool close
    )
    {
        if (booking is null && version is null or 0 && !close)
            return true;
        if (
            booking is null
            || booking.Id != orderId
            || booking.OwnerUserId != owner
            || !booking.HasConsistentMutationOwner
            || booking.ProviderOrderId != providerOrder
            || booking.CurrentCreation?.Outcome != BookingCreationOutcome.Matches
            || reverse && close
            || !(
                booking.Status == BookingStatus.Held && version == HeldVersion
                || !reverse
                    && booking.Status == BookingStatus.Cancelled
                    && version == HeldVersion + 1
            )
        )
            throw new LocalStackRefusal("The fixture stream does not match this owner and phase.");
        return close && booking.Status == BookingStatus.Held;
    }

    internal static object[] HeldEvents(
        Guid orderId,
        Guid owner,
        string providerOrder,
        bool reverse,
        DateTimeOffset now,
        IBookingPassengerPartyProtector protector
    )
    {
        var departure = now.AddDays(2).ToOffset(TimeSpan.FromHours(3));
        var segment = Segment
            .Create(
                IataCode.Create(reverse ? "LED" : "SVO").Value,
                IataCode.Create(reverse ? "SVO" : "LED").Value,
                departure,
                departure.AddHours(2),
                "ZZ",
                "101",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary.Create([Slice.Create([segment]).Value]).Value;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var party = BookableOfferParty
            .Create(
                [
                    new(
                        SupplierPassengerReference.Create("pas_fictional").Value,
                        BookingPassengerKind.Adult
                    ),
                ],
                DateOnly.FromDateTime(departure.DateTime),
                true,
                false
            )
            .Value;
        var binding = QuoteBindingFactory.Create(party, 1, null).Value;
        var passenger = BookingPassenger
            .Create(
                binding.Slots[0].Id,
                BookingPassengerDetails
                    .CreateRaw(
                        "mr",
                        "Fictional",
                        "Traveler",
                        new DateOnly(1990, 1, 1),
                        "male",
                        "traveler@fictional.invalid",
                        "+12025550123",
                        today
                    )
                    .Value
            )
            .Value;
        var passengers = new EquatableArray<BookingPassenger>([passenger]);
        if (binding.ValidatePassengers(passengers, today).IsError)
            throw new LocalStackRefusal("The fictional passenger does not match the quote.");
        var protectedParty = protector.Protect(
            new(orderId, owner, binding.Revision, 1),
            passengers
        );
        if (protectedParty.IsError)
            throw new LocalStackRefusal("The owned Flights protection service is unavailable.");
        var fare = Money.Create(100m, CurrencyCode.Create("RUB").Value).Value;
        var until = now.AddHours(1);
        var quote = new OfferQuoted(
            OfferId.New(),
            itinerary,
            fare,
            until,
            "off_fictional",
            now,
            QuoteBinding: binding
        );
        var purchase = new BookingPurchaseQuoted(
            BookingPurchase.Create(binding.Revision, owner, fare, until, []).Value,
            now
        );
        var booking = new BookingAggregate();
        booking.Apply(quote);
        booking.Apply(purchase);
        var start = booking
            .DecideCreationStart(
                owner,
                Guid.NewGuid(),
                new string('a', 64),
                binding.Revision,
                protectedParty.Value,
                Guid.NewGuid(),
                now,
                false
            )
            .Value.Event!;
        booking.Apply(start);
        var actual = new BookedOrderFacts(
            providerOrder,
            quote.ProviderRef,
            itinerary,
            new([passenger.Id.Value]),
            new([]),
            fare,
            true,
            until
        );
        var matched = booking
            .DecideCreationObservation(
                start.AttemptId,
                1,
                new(
                    BookingCreationOutcome.Matches,
                    actual,
                    providerOrder,
                    true,
                    true,
                    "FictionalFixture",
                    now
                ),
                now
            )
            .Value.Event!;
        return
        [
            quote,
            purchase,
            start,
            matched,
            new OfferHeldV3(
                providerOrder,
                protectedParty.Value,
                until,
                now,
                owner,
                binding.Revision,
                1
            ),
            new BookingMutationCoordinationEnabled(now),
        ];
    }
}
