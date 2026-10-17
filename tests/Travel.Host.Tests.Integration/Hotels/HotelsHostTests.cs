using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Host.Tests.Integration.Hotels;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class HotelsHostTests
{
    [Fact]
    public async Task Owning_key_and_commit_failure_refuse_ACK_without_leaking_processing_work_while_foreign_store_outage_is_isolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var privacy = fixture
            .Services.GetRequiredService<IOptions<HotelPiiProtectionOptions>>()
            .Value;
        var originalRing = privacy.KeyRingPath;
        var body = Event(
            "evt_owner_fault_fictional",
            "stays.booking.created",
            new JsonObject
            {
                ["quote_id"] = "quo_unmatched_fictional",
                ["booking_id"] = "bok_unmatched_fictional",
            }
        );
        privacy.KeyRingPath = Path.Combine(originalRing!, "missing_owned_fixture_ring");
        try
        {
            using var unavailable = await SendWebhook(fixture.Client, body, ct);
            unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await db.Inbox.CountAsync(ct)).ShouldBe(0);
        }
        finally
        {
            privacy.KeyRingPath = originalRing;
        }
        // Test-only transaction probe in this disposable DB: a durable message committed outside the failed inbox transaction would remain counted even after a worker drained it.
        const string probe =
            "CREATE TABLE hotels.fixture_observation_publish_probe (observed integer NOT NULL); CREATE FUNCTION hotels.fixture_capture_observation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.message_type LIKE '%ProcessHotelObservation%' THEN INSERT INTO hotels.fixture_observation_publish_probe VALUES (1); END IF; RETURN NEW; END $$; CREATE TRIGGER fixture_capture_observation AFTER INSERT ON public.wolverine_incoming_envelopes FOR EACH ROW EXECUTE FUNCTION hotels.fixture_capture_observation(); CREATE FUNCTION hotels.fixture_reject_observation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'fictional owner commit fault'; END $$; CREATE TRIGGER fixture_reject_observation BEFORE INSERT ON hotels.observation_inbox FOR EACH ROW EXECUTE FUNCTION hotels.fixture_reject_observation();";
        await db.Database.ExecuteSqlRawAsync(probe, ct);
        try
        {
            using var failed = await SendWebhook(fixture.Client, body, ct);
            failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await db.Inbox.CountAsync(ct)).ShouldBe(0);
            (
                await db
                    .Database.SqlQueryRaw<int>(
                        "SELECT count(*)::integer AS \"Value\" FROM hotels.fixture_observation_publish_probe"
                    )
                    .SingleAsync(ct)
            ).ShouldBe(0);
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER fixture_reject_observation ON hotels.observation_inbox; ALTER TABLE flights.webhook_inbox RENAME TO fixture_unavailable_webhook_inbox;",
                ct
            );
            try
            {
                using var accepted = await SendWebhook(fixture.Client, body, ct);
                accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
                using var duplicate = await SendWebhook(fixture.Client, body, ct);
                duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await db.Inbox.CountAsync(ct)).ShouldBe(1);
                (
                    await db
                        .Database.SqlQueryRaw<int>(
                            "SELECT count(*)::integer AS \"Value\" FROM hotels.fixture_observation_publish_probe"
                        )
                        .SingleAsync(ct)
                ).ShouldBe(1);
                (
                    await db
                        .Database.SqlQueryRaw<int>(
                            "SELECT count(*)::integer AS \"Value\" FROM flights.fixture_unavailable_webhook_inbox"
                        )
                        .SingleAsync(ct)
                ).ShouldBe(0);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE flights.fixture_unavailable_webhook_inbox RENAME TO webhook_inbox;",
                    ct
                );
            }
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS fixture_reject_observation ON hotels.observation_inbox; DROP FUNCTION hotels.fixture_reject_observation(); DROP TRIGGER fixture_capture_observation ON public.wolverine_incoming_envelopes; DROP FUNCTION hotels.fixture_capture_observation(); DROP TABLE hotels.fixture_observation_publish_probe;",
                ct
            );
        }
    }

    [Fact]
    public async Task Correlated_created_event_with_missing_or_invalid_booking_facts_is_rejected_without_positive_state_or_read()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        var id = await Quote(fixture.Client, 1, ct);
        fixture.Supplier.NextCreate = "pending";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/hotels/reservations/{id}/purchase"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    actionId = Guid.NewGuid(),
                    quoteRevision = 1,
                    consent = true,
                    party = new
                    {
                        guests = new[] { new { givenName = "Fictional", familyName = "First" } },
                        email = "fictional@example.invalid",
                        phone = "+44000000000",
                        adultsConfirmed = true,
                        checkInConfirmed = true,
                    },
                }
            ),
        };
        request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var purchase = await fixture.Client.SendAsync(request, ct);
        purchase.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var bookingId in new string?[] { null, ".", "..", "invalid/id" })
        {
            var body = new JsonObject { ["quote_id"] = fixture.Supplier.LastQuoteId };
            if (bookingId is not null)
                body["booking_id"] = bookingId;
            using var response = await SendWebhook(
                fixture.Client,
                Event("evt_incomplete_created", "stays.booking.created", body),
                ct
            );
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var row = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        var state = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!;
        state.Outcome.ShouldBe(ReservationOutcome.Pending);
        state.EverReserved.ShouldBeFalse();
        state.BoundBookingId.ShouldBeNull();
        (await db.Inbox.AsNoTracking().CountAsync(ct)).ShouldBe(0);
        fixture.Supplier.Reads.ShouldBe(0);
        fixture.Supplier.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task Fixture_configuration_is_Testing_memory_only_and_has_no_saved_secret_provider()
    {
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(TestContext.Current.CancellationToken);
        fixture.IsolatedConfiguration.ShouldBeTrue();
        fixture.Supplier.Starts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Early_authenticated_facts_survive_the_original_inflight_sender_result(
        bool positive
    )
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        var id = await Quote(fixture.Client, 1, ct);
        fixture.Supplier.NextCreate = positive ? "lost" : "pending";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Supplier.CreateGate = gate;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/hotels/reservations/{id}/purchase"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    actionId = Guid.NewGuid(),
                    quoteRevision = 1,
                    consent = true,
                    party = new
                    {
                        guests = new[] { new { givenName = "Fictional", familyName = "First" } },
                        email = "fictional@example.invalid",
                        phone = "+44000000000",
                        adultsConfirmed = true,
                        checkInConfirmed = true,
                    },
                }
            ),
        };
        request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        var pending = fixture.Client.SendAsync(request, ct);
        await fixture.Supplier.CreateStarted.Task.WaitAsync(ct);
        try
        {
            var obj = new JsonObject { ["quote_id"] = fixture.Supplier.LastQuoteId };
            if (positive)
                obj["booking_id"] = fixture.Supplier.LastBookingId;
            using var callback = await SendWebhook(
                fixture.Client,
                Event(
                    "evt_early_fictional",
                    positive ? "stays.booking.created" : "stays.booking_creation_failed",
                    obj
                ),
                ct
            );
            callback.StatusCode.ShouldBe(HttpStatusCode.OK);
            var observed = false;
            for (var i = 0; i < 100; i++)
            {
                using var scope = fixture.Services.CreateScope();
                var row = await scope
                    .ServiceProvider.GetRequiredService<HotelsDbContext>()
                    .Reservations.AsNoTracking()
                    .SingleAsync(r => r.Id == id, ct);
                var state = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!;
                if (positive ? state.EverReserved : state.NoCreationObserved)
                {
                    row.SenderFinished.ShouldBeFalse();
                    observed = true;
                    break;
                }
                await Task.Delay(100, ct);
            }
            observed.ShouldBeTrue();
        }
        finally
        {
            gate.TrySetResult();
        }
        using var reply = await pending;
        reply.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = JsonNode.Parse(await reply.Content.ReadAsStringAsync(ct))!;
        result["everReserved"]!.GetValue<bool>().ShouldBe(positive);
        if (positive)
            result["outcome"]!
                .GetValue<string>()
                .ShouldBeOneOf("Confirmed", "ReservedAwaitingDetails");
        else
            result["outcome"]!.GetValue<string>().ShouldBe("NotCreated");
        fixture.Supplier.Creates.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_real_Host_retains_no_ID_positive_fact_and_one_late_identity_window_after_escalation(
        bool inconclusiveReads
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = new HotelsHostFixture { Clock = clock };
        await fixture.StartAsync(ct);
        var id = await Quote(fixture.Client, 1, ct);
        fixture.Supplier.NextCreate = "reserved";
        var action = Guid.NewGuid();
        using var purchase = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/hotels/reservations/{id}/purchase"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    actionId = action,
                    quoteRevision = 1,
                    consent = true,
                    party = new
                    {
                        guests = new[] { new { givenName = "Fictional", familyName = "First" } },
                        email = "fictional@example.invalid",
                        phone = "+44000000000",
                        adultsConfirmed = true,
                        checkInConfirmed = true,
                    },
                }
            ),
        };
        purchase.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var result = await fixture.Client.SendAsync(purchase, ct);
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonNode.Parse(await result.Content.ReadAsStringAsync(ct))!["outcome"]!
            .GetValue<string>()
            .ShouldBe("ReservedAwaitingDetails");
        fixture.Supplier.Reads.ShouldBe(0);
        clock.Advance(TimeSpan.FromHours(2));
        await fixture.RestartAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<HotelObservationRunner>();
            await runner.RunAsync(new(id, true, clock.GetUtcNow()), ct);
            await runner.RunAsync(new(id, false, clock.GetUtcNow()), ct); // No identity: zero GET/list discovery.
        }
        fixture.Supplier.Creates.ShouldBe(1);
        fixture.Supplier.Reads.ShouldBe(0);
        fixture.Services.GetRequiredService<IOptions<HotelsOptions>>().Value.BookingEnabled = false;
        var refusedId = Guid.NewGuid();
        using (
            var refused = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/hotels/reservations/{refusedId}/purchase"
            )
            {
                Content = JsonContent.Create(new { fictional = true }),
            }
        )
        {
            refused.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
            using var disabled = await fixture.Client.SendAsync(refused, ct);
            disabled.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }
        using (var check = fixture.Services.CreateScope())
            (
                await check
                    .ServiceProvider.GetRequiredService<HotelsDbContext>()
                    .Reservations.AnyAsync(r => r.Id == refusedId, ct)
            ).ShouldBeFalse();
        var body = Event(
            "evt_late_fictional_id",
            "stays.booking.created",
            new JsonObject
            {
                ["quote_id"] = fixture.Supplier.LastQuoteId,
                ["booking_id"] = fixture.Supplier.LastBookingId,
            }
        );
        using var callback = await SendWebhook(fixture.Client, body, ct);
        callback.StatusCode.ShouldBe(HttpStatusCode.OK);
        HotelReservationEntity saved = null!;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var scope = fixture.Services.CreateScope();
            saved = await scope
                .ServiceProvider.GetRequiredService<HotelsDbContext>()
                .Reservations.AsNoTracking()
                .SingleAsync(r => r.Id == id, ct);
            if (
                JsonSerializer.Deserialize<ReservationState>(saved.StateJson)!.BoundBookingId
                is not null
            )
                break;
            await Task.Delay(100, ct);
        }
        var first = JsonSerializer.Deserialize<ReservationState>(saved.StateJson)!;
        first.BoundBookingId.ShouldBe(fixture.Supplier.LastBookingId);
        first.FirstKnownAt.ShouldBe(clock.GetUtcNow());
        first.ReadUntil.ShouldBe(clock.GetUtcNow().AddMinutes(5));
        fixture.Supplier.InconclusiveReads = inconclusiveReads;
        using (
            var held = fixture
                .Services.GetRequiredService<HotelCapacity>()
                .TryAcquire("duffel-stays", recovery: true)
        )
        {
            held.ShouldNotBeNull();
            using var scope = fixture.Services.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<HotelObservationRunner>()
                .RunAsync(new(id, false, clock.GetUtcNow()), ct);
            var denied = await scope
                .ServiceProvider.GetRequiredService<HotelsDbContext>()
                .Reservations.AsNoTracking()
                .SingleAsync(r => r.Id == id, ct);
            JsonSerializer
                .Deserialize<ReservationState>(denied.StateJson)!
                .ReadsClaimed.ShouldBe(0);
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        using (var scope = fixture.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<HotelObservationRunner>()
                .RunAsync(new(id, false, clock.GetUtcNow()), ct);
        fixture.Supplier.Reads.ShouldBe(1);
        if (inconclusiveReads)
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            using (var scope = fixture.Services.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<HotelObservationRunner>()
                    .RunAsync(new(id, false, clock.GetUtcNow()), ct);
            await fixture.RestartAsync();
            clock.Advance(TimeSpan.FromSeconds(120));
            using (var scope = fixture.Services.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<HotelObservationRunner>()
                    .RunAsync(new(id, false, clock.GetUtcNow()), ct);
            fixture.Supplier.Reads.ShouldBe(3);
        }
        await fixture.RestartAsync();
        using var duplicate = await SendWebhook(fixture.Client, body, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var scope = fixture.Services.CreateScope())
        {
            var row = await scope
                .ServiceProvider.GetRequiredService<HotelsDbContext>()
                .Reservations.AsNoTracking()
                .SingleAsync(r => r.Id == id, ct);
            var state = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!;
            state.Outcome.ShouldBe(
                inconclusiveReads
                    ? ReservationOutcome.ReservedAwaitingDetails
                    : ReservationOutcome.Confirmed
            );
            if (!inconclusiveReads)
                state.Review.ShouldBeEmpty();
            state.ReadsClaimed.ShouldBe(inconclusiveReads ? 3 : 1);
            state.ReadUntil.ShouldBe(first.ReadUntil);
            await scope
                .ServiceProvider.GetRequiredService<HotelObservationRunner>()
                .RunAsync(new(id, false, clock.GetUtcNow()), ct);
        }
        fixture.Supplier.Reads.ShouldBe(inconclusiveReads ? 3 : 1);
        fixture.Supplier.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task Shared_route_validates_before_historical_dedup_and_isolates_unrelated_private_stores()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        using var seedScope = fixture.Services.CreateScope();
        var flights = seedScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        const string eventId = "evt_saved_flights_history";
        var inboxId = Guid.NewGuid();
        var historical = Encoding.UTF8.GetBytes(
            "{\"id\":\"evt_saved_flights_history\",\"type\":\"order.airline_initiated_change\",\"api_version\":\"v2\",\"object\":{\"id\":\"ord_fictional_history\"}}"
        );
        var protectedPayload = TestPii
            .Webhooks.Protect(
                inboxId,
                "duffel",
                eventId,
                "order.airline_initiated_change",
                historical
            )
            .Value;
        flights.WebhookInbox.Add(
            new()
            {
                Id = inboxId,
                Source = "duffel",
                EventId = eventId,
                EventType = "order.airline_initiated_change",
                RawPayload = protectedPayload,
                Signature = "retained-original-signature",
                ReceivedAt = DateTimeOffset.UtcNow,
            }
        );
        var legacyId = Guid.NewGuid();
        var legacyEventId = eventId + "_legacy";
        var legacyBody = Encoding
            .UTF8.GetString(historical)
            .Replace(eventId, legacyEventId, StringComparison.Ordinal);
        flights.WebhookInbox.Add(
            new()
            {
                Id = legacyId,
                Source = "duffel",
                EventId = legacyEventId,
                EventType = "order.airline_initiated_change",
                RawPayload = legacyBody,
                Signature = "legacy-original-signature",
                ReceivedAt = DateTimeOffset.UtcNow,
            }
        );
        await flights.SaveChangesAsync(ct);
        // The existing column is jsonb: compare to the committed representation, not pre-insert JSON formatting.
        var retainedBefore = await flights
            .WebhookInbox.AsNoTracking()
            .SingleAsync(i => i.Id == inboxId, ct);
        var retainedLegacy = await flights
            .WebhookInbox.AsNoTracking()
            .SingleAsync(i => i.Id == legacyId, ct);
        seedScope.Dispose();
        await fixture.RestartAsync();
        using var scope = fixture.Services.CreateScope();
        flights = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var hotels = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var current = Event(
            eventId,
            "order.airline_initiated_change_detected",
            new JsonObject { ["id"] = "ord_fictional_history" }
        );
        using var duplicate = await SendWebhook(fixture.Client, current, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unchanged = await flights
            .WebhookInbox.AsNoTracking()
            .SingleAsync(i => i.Id == inboxId, ct);
        (unchanged.RawPayload == retainedBefore.RawPayload).ShouldBeTrue();
        unchanged.Source.ShouldBe("duffel");
        unchanged.EventType.ShouldBe("order.airline_initiated_change");
        unchanged.Signature.ShouldBe("retained-original-signature");
        var entry = new WebhookInboxEntry(
            unchanged.Id,
            unchanged.EventType,
            unchanged.RawPayload,
            unchanged.ReceivedAt,
            null,
            unchanged.Source,
            unchanged.EventId
        );
        TestPii.Webhooks.Decode(entry).Value.Bytes.ShouldBe(historical);
        TestPii.WebhookReader.Read(entry).Value.Kind.ShouldBe(BookingWebhookKind.AirlineChanged);
        TestPii
            .Webhooks.Decode(entry with { Source = "different-purpose-source" })
            .IsError.ShouldBeTrue();
        using var legacyDuplicate = await SendWebhook(
            fixture.Client,
            Event(
                legacyEventId,
                "order.airline_initiated_change_detected",
                new JsonObject { ["id"] = "ord_fictional_history" }
            ),
            ct
        );
        legacyDuplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unchangedLegacy = await flights
            .WebhookInbox.AsNoTracking()
            .SingleAsync(i => i.Id == legacyId, ct);
        (unchangedLegacy.RawPayload == retainedLegacy.RawPayload).ShouldBeTrue();
        unchangedLegacy.Source.ShouldBe("duffel");
        unchangedLegacy.EventType.ShouldBe("order.airline_initiated_change");
        unchangedLegacy.Signature.ShouldBe("legacy-original-signature");
        var legacyEntry = new WebhookInboxEntry(
            legacyId,
            unchangedLegacy.EventType,
            unchangedLegacy.RawPayload,
            unchangedLegacy.ReceivedAt,
            null,
            unchangedLegacy.Source,
            unchangedLegacy.EventId
        );
        TestPii
            .Webhooks.Decode(legacyEntry)
            .Value.Bytes.ShouldBe(Encoding.UTF8.GetBytes(retainedLegacy.RawPayload));
        TestPii
            .WebhookReader.Read(legacyEntry)
            .Value.Kind.ShouldBe(BookingWebhookKind.AirlineChanged);
        using var legacyNetwork = await SendWebhook(fixture.Client, legacyBody, ct);
        legacyNetwork.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var oldNetwork = await SendWebhook(
            fixture.Client,
            Encoding.UTF8.GetString(historical),
            ct
        );
        oldNetwork.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Flights normal Host keys are deliberately unconfigured. A Hotels owner never needs them.
        var unmatched = Event(
            "evt_unmatched_hotel",
            "stays.booking.created",
            new JsonObject
            {
                ["quote_id"] = "quo_no_such_purchase",
                ["booking_id"] = "bok_unmatched_fictional",
            }
        );
        using var accepted = await SendWebhook(fixture.Client, unmatched, ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await flights.WebhookInbox.AsNoTracking().CountAsync(ct)).ShouldBe(2);
        (
            await hotels
                .Inbox.AsNoTracking()
                .CountAsync(i => i.EventId == "evt_unmatched_hotel", ct)
        ).ShouldBe(1);
        using var invalidMode = await SendWebhook(
            fixture.Client,
            unmatched.Replace(
                "\"live_mode\":false",
                "\"live_mode\":true",
                StringComparison.Ordinal
            ),
            ct
        );
        invalidMode.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var invalidAccount = await SendWebhook(
            fixture.Client,
            unmatched.Replace(
                HotelsHostFixture.Account,
                "org_another_fictional",
                StringComparison.Ordinal
            ),
            ct
        );
        invalidAccount.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await hotels.Inbox.AsNoTracking().CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Real_Host_complete_purchase_and_signed_cancellation_use_configured_account_and_local_reads()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        var id = await Quote(fixture.Client, 2, ct);
        using var purchase = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/hotels/reservations/{id}/purchase"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    actionId = Guid.NewGuid(),
                    quoteRevision = 1,
                    consent = true,
                    party = new
                    {
                        guests = new[]
                        {
                            new { givenName = "Fictional", familyName = "First" },
                            new { givenName = "Fictional", familyName = "Second" },
                        },
                        email = "fictional@example.invalid",
                        phone = "+44000000000",
                        adultsConfirmed = true,
                        checkInConfirmed = true,
                    },
                }
            ),
        };
        purchase.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var reply = await fixture.Client.SendAsync(purchase, ct);
        var content = await reply.Content.ReadAsStringAsync(ct);
        reply.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        var result = JsonNode.Parse(content)!;
        result["outcome"]!.GetValue<string>().ShouldBe("Confirmed");
        fixture.Supplier.LastGuestCount.ShouldBe(2);
        fixture.Supplier.Creates.ShouldBe(1);
        var booking = result["facts"]!["bookingId"]!.GetValue<string>();
        var callback = Event(
            "evt_cancelled_fictional",
            "stays.booking.cancelled",
            new JsonObject { ["id"] = booking }
        );
        using var accepted = await SendWebhook(fixture.Client, callback, ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var duplicate = await SendWebhook(fixture.Client, callback, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var row = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id, ct);
            row.ProviderAccount.ShouldBe(HotelsHostFixture.Account);
            if (row.StateJson.Contains("Cancelled", StringComparison.Ordinal))
                break;
            await Task.Delay(100, ct);
        }
        var reads = fixture.Supplier.Reads;
        using var detail = new HttpRequestMessage(HttpMethod.Get, $"/api/hotels/reservations/{id}");
        detail.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var current = await fixture.Client.SendAsync(detail, ct);
        var state = JsonNode.Parse(await current.Content.ReadAsStringAsync(ct))!;
        state["outcome"]!.GetValue<string>().ShouldBe("Cancelled");
        state["active"]!.GetValue<bool>().ShouldBeFalse();
        fixture.Supplier.Reads.ShouldBe(reads);
        (
            await db
                .Inbox.AsNoTracking()
                .CountAsync(i => i.EventId == "evt_cancelled_fictional", ct)
        ).ShouldBe(1);
        var historical = JsonNode.Parse(callback)!;
        historical["object"] = historical["data"]!["object"]!.DeepClone();
        historical.AsObject().Remove("data");
        using var rejected = await SendWebhook(fixture.Client, historical.ToJsonString(), ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var openapi = await fixture.Client.GetAsync("/openapi/v1.json", ct);
        (await openapi.Content.ReadAsStringAsync(ct)).ShouldContain(
            "/api/hotels/reservations/{id}/purchase"
        );
    }

    public static async Task<Guid> Quote(HttpClient client, int adults, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var search = await client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "london",
                checkIn = today.AddDays(7),
                checkOut = today.AddDays(9),
                adults,
            },
            ct
        );
        var content = await search.Content.ReadAsStringAsync(ct);
        search.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        var found = JsonNode.Parse(content)!;
        using var rates = await client.PostAsJsonAsync(
            "/api/hotels/rates",
            new
            {
                searchId = found["id"]!.GetValue<Guid>(),
                selectionKey = found["result"]!["items"]![0]!["selectionKey"]!.GetValue<string>(),
            },
            ct
        );
        var loaded = JsonNode.Parse(await rates.Content.ReadAsStringAsync(ct))!;
        var id = Guid.NewGuid();
        using var quote = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/hotels/reservations/{id}/quote"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    ratesId = loaded["id"]!.GetValue<Guid>(),
                    selectionKey = loaded["result"]!["items"]![0]![
                        "selectionKey"
                    ]!.GetValue<string>(),
                    expectedRevision = (int?)null,
                }
            ),
        };
        quote.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var reply = await client.SendAsync(quote, ct);
        reply.StatusCode.ShouldBe(HttpStatusCode.OK, await reply.Content.ReadAsStringAsync(ct));
        return id;
    }

    public static string Event(string id, string type, JsonObject data) =>
        new JsonObject
        {
            ["id"] = id,
            ["type"] = type,
            ["api_version"] = "v2",
            ["live_mode"] = false,
            ["identity_organisation_id"] = HotelsHostFixture.Account,
            ["created_at"] = DateTimeOffset.UtcNow,
            ["data"] = new JsonObject { ["object"] = data },
        }.ToJsonString();

    public static async Task<HttpResponseMessage> SendWebhook(
        HttpClient client,
        string body,
        CancellationToken ct
    )
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var timestamp = DateTimeOffset
            .UtcNow.ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Convert.ToHexString(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(HotelsHostFixture.Secret),
                Encoding.UTF8.GetBytes(timestamp + ".").Concat(bytes).ToArray()
            )
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/duffel")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Headers.Add("X-Duffel-Signature", $"t={timestamp},v1={signature}");
        return await client.SendAsync(request, ct);
    }
}
