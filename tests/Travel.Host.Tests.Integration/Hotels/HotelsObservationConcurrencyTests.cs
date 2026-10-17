using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Xunit;

namespace Travel.Host.Tests.Integration.Hotels;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class HotelsObservationConcurrencyTests
{
    [Fact]
    public async Task One_observation_CAS_conflict_then_retry_keeps_processed_marker_and_one_immediate_read_work()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct);
        var id = await HotelsHostTests.Quote(fixture.Client, 1, ct);
        fixture.Supplier.NextCreate = "reserved";
        fixture.Supplier.InconclusiveReads = true;
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
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var ddl =
            $"CREATE SEQUENCE hotels.fixture_observation_cas_once; CREATE FUNCTION hotels.fixture_observation_cas() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.id = '{id}'::uuid AND nextval('hotels.fixture_observation_cas_once') = 1 THEN RETURN NULL; END IF; RETURN NEW; END $$; CREATE TRIGGER fixture_observation_cas BEFORE UPDATE ON hotels.reservations FOR EACH ROW EXECUTE FUNCTION hotels.fixture_observation_cas(); CREATE TABLE hotels.fixture_read_work_probe (observed integer NOT NULL); CREATE FUNCTION hotels.fixture_capture_read_work() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.message_type LIKE '%HotelWork%' AND (NEW.execution_time IS NULL OR NEW.execution_time < now() + interval '2 seconds') THEN INSERT INTO hotels.fixture_read_work_probe VALUES (1); END IF; RETURN NEW; END $$; CREATE TRIGGER fixture_capture_read_work AFTER INSERT ON public.wolverine_incoming_envelopes FOR EACH ROW EXECUTE FUNCTION hotels.fixture_capture_read_work();";
        await db.Database.ExecuteSqlRawAsync(ddl, ct);
        try
        {
            var body = HotelsHostTests.Event(
                "evt_cas_fictional",
                "stays.booking.created",
                new JsonObject
                {
                    ["quote_id"] = fixture.Supplier.LastQuoteId,
                    ["booking_id"] = fixture.Supplier.LastBookingId,
                }
            );
            using var callback = await HotelsHostTests.SendWebhook(fixture.Client, body, ct);
            callback.StatusCode.ShouldBe(HttpStatusCode.OK);
            HotelInboxEntity? inbox = null;
            ReservationState? state = null;
            var pending = -1;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                inbox = await db
                    .Inbox.AsNoTracking()
                    .SingleAsync(i => i.EventId == "evt_cas_fictional", ct);
                var row = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == id, ct);
                state = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!;
                pending = await db
                    .Database.SqlQueryRaw<int>(
                        "SELECT count(*)::integer AS \"Value\" FROM public.wolverine_incoming_envelopes WHERE message_type LIKE '%ProcessHotelObservation%' AND status <> 'Handled' AND (execution_time IS NULL OR execution_time <= now())"
                    )
                    .SingleAsync(ct);
                if (
                    inbox.ProcessedAt is not null
                    && state.ReadsClaimed > 0
                    && fixture.Supplier.Reads == 1
                    && pending == 0
                )
                    break;
                await Task.Delay(100, ct);
            }
            var work = await db
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::integer AS \"Value\" FROM hotels.fixture_read_work_probe"
                )
                .SingleAsync(ct);
            inbox!.ProcessedAt.ShouldNotBeNull(
                $"The successful CAS retry must retain its inbox marker; immediate read work count={work}."
            );
            work.ShouldBe(1);
            state!.BoundBookingId.ShouldBe(fixture.Supplier.LastBookingId);
            state.ReadsClaimed.ShouldBe(1);
            fixture.Supplier.Creates.ShouldBe(1);
            fixture.Supplier.Reads.ShouldBe(1);
            pending.ShouldBe(0);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER fixture_capture_read_work ON public.wolverine_incoming_envelopes; DROP FUNCTION hotels.fixture_capture_read_work(); DROP TABLE hotels.fixture_read_work_probe; DROP TRIGGER fixture_observation_cas ON hotels.reservations; DROP FUNCTION hotels.fixture_observation_cas(); DROP SEQUENCE hotels.fixture_observation_cas_once;",
                ct
            );
        }
    }
}
