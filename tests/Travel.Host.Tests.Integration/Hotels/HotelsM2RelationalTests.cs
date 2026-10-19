using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Host.Tests.Integration.Hotels;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class HotelsM2RelationalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Owner = Guid.Parse(HotelsHostFixture.Owner);

    [Fact]
    public async Task Request_abort_after_owned_effect_does_not_cancel_or_repeat_the_committed_sender()
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.HotelbedsWire.AfterEffect = async () =>
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(Ct);
        };
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var scope = fixture.Services.CreateScope();
        var purchase = Purchase();
        var sender = scope
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .PurchaseAsync(Owner, id, purchase, request.Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            await request.CancelAsync();
            var retained = await Row(fixture, id);
            retained.AttemptId.ShouldNotBeNull();
            retained.SenderFinished.ShouldBeFalse();
            (await Buy(fixture, id, purchase)).Value.AttemptId.ShouldBe(retained.AttemptId);
            fixture.HotelbedsWire.Creates.ShouldBe(1);
        }
        finally
        {
            release.TrySetResult();
        }
        (await sender).Value.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        (await Row(fixture, id)).SenderFinished.ShouldBeTrue();
        fixture.HotelbedsWire.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task V2_Duffel_admission_uses_the_shared_signed_ingress_and_only_Hotels_inbox()
    {
        await using var fixture = await Start(TimeProvider.System);
        var id = await Quote(fixture, "duffel-stays");
        fixture.DuffelWire.Unknown = true;
        (await Buy(fixture, id, Purchase())).Value.Outcome.ShouldBe(ReservationOutcome.Unknown);
        var admitted = await Row(fixture, id);
        admitted.State.QuoteContractVersion.ShouldBe(2);
        var body = HotelsHostTests.Event(
            "evt_m2_owned_" + Guid.NewGuid().ToString("N"),
            "stays.booking.created",
            new JsonObject
            {
                ["quote_id"] = admitted.SourcePreparationReference,
                ["booking_id"] = "bok_m2_owned_late",
            }
        );
        using (
            var invalid = new HttpRequestMessage(HttpMethod.Post, "/webhooks/duffel")
            {
                Content = new StringContent(body),
            }
        )
        {
            invalid.Headers.Add("X-Duffel-Signature", "t=1,v1=invalid");
            using var rejected = await fixture.Client.SendAsync(invalid, Ct);
            rejected.IsSuccessStatusCode.ShouldBeFalse();
        }
        using (var accepted = await HotelsHostTests.SendWebhook(fixture.Client, body, Ct))
            accepted.EnsureSuccessStatusCode();
        using (var duplicate = await HotelsHostTests.SendWebhook(fixture.Client, body, Ct))
            duplicate.EnsureSuccessStatusCode();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (
            (await Row(fixture, id)).State.BoundBookingId is null
            && DateTimeOffset.UtcNow < deadline
        )
            await Task.Delay(50, Ct);
        var saved = await Row(fixture, id);
        saved.State.BoundBookingId.ShouldBe("bok_m2_owned_late");
        saved.State.EverReserved.ShouldBeTrue();
        saved.State.AdmissionBinding.ShouldBe(admitted.State.AdmissionBinding);
        using var scope = fixture.Services.CreateScope();
        (
            await scope.ServiceProvider.GetRequiredService<HotelsDbContext>().Inbox.CountAsync(Ct)
        ).ShouldBe(1);
        (
            await scope
                .ServiceProvider.GetRequiredService<Travel.Modules.Flights.Infrastructure.Persistence.FlightsDbContext>()
                .WebhookInbox.CountAsync(Ct)
        ).ShouldBe(0);
        fixture.Client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            "fictional-hotels-owner"
        );
        using var nonexistent = await fixture.Client.PostAsync(
            "/webhooks/hotelbeds",
            new StringContent("{}"),
            Ct
        );
        nonexistent.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        fixture.DuffelWire.Creates.ShouldBe(1);
        fixture.HotelbedsWire.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Real_sql_admission_failure_rolls_back_claim_and_durable_work_together()
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var ownedFaultSql =
            $"CREATE TABLE hotels.m2_work_probe (observed integer NOT NULL); CREATE FUNCTION hotels.m2_capture_work() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.message_type LIKE '%HotelWork%' THEN INSERT INTO hotels.m2_work_probe VALUES (1); END IF; RETURN NEW; END $$; CREATE TRIGGER m2_capture_work AFTER INSERT ON public.wolverine_incoming_envelopes FOR EACH ROW EXECUTE FUNCTION hotels.m2_capture_work(); CREATE FUNCTION hotels.m2_reject_claim() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.id = '{id}'::uuid AND NEW.attempt_id IS NOT NULL AND EXISTS(SELECT 1 FROM hotels.m2_work_probe) THEN RAISE EXCEPTION 'owned M2 rollback'; END IF; RETURN NEW; END $$; CREATE TRIGGER m2_reject_claim BEFORE UPDATE ON hotels.reservations FOR EACH ROW EXECUTE FUNCTION hotels.m2_reject_claim();";
        await db.Database.ExecuteSqlRawAsync(ownedFaultSql, Ct);
        try
        {
            (await Buy(fixture, id, Purchase())).IsError.ShouldBeTrue();
            (await Row(fixture, id)).AttemptId.ShouldBeNull();
            (
                await db
                    .Database.SqlQueryRaw<int>(
                        "SELECT count(*)::integer AS \"Value\" FROM hotels.m2_work_probe"
                    )
                    .SingleAsync(Ct)
            ).ShouldBe(0);
            fixture.HotelbedsWire.Creates.ShouldBe(0);
            using var capacity = fixture
                .Services.GetRequiredService<IHotelCapacity>()
                .TryAcquire("hotelbeds");
            capacity.ShouldNotBeNull();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER m2_reject_claim ON hotels.reservations; DROP FUNCTION hotels.m2_reject_claim();",
                Ct
            );
        }
        (await Buy(fixture, id, Purchase())).IsError.ShouldBeFalse();
        (
            await db
                .Database.SqlQueryRaw<int>(
                    "SELECT count(*)::integer AS \"Value\" FROM hotels.m2_work_probe"
                )
                .SingleAsync(Ct)
        ).ShouldBe(1);
        fixture.HotelbedsWire.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task Pre_M2_json_context_and_history_survive_real_sql_and_cold_Host_without_invented_facts()
    {
        await using var fixture = await Start();
        var json = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "HotelsFixtures/m1-reservation.json"),
            Ct
        );
        var context = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "HotelsFixtures/m1-duffel-context.json"),
            Ct
        );
        json.ShouldNotContain("AdmissionBinding");
        var legacy = JsonSerializer.Deserialize<HotelReservation>(json)!;
        legacy.Id = Guid.NewGuid();
        legacy.Version = 0;
        var terms = JsonSerializer.Serialize(legacy.Terms);
        var purchase = Purchase() with { ActionId = legacy.ActionId!.Value };
        using (var scope = fixture.Services.CreateScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<IHotelPrivateProtector>();
            legacy.ProtectedContext = protector
                .Protect(context, HotelReservationService.Binding(legacy, "context"))
                .Value;
            legacy.ProtectedParty = protector
                .Protect(
                    JsonSerializer.Serialize(purchase.Party),
                    HotelReservationService.Binding(legacy, "party")
                )
                .Value;
            (
                await scope
                    .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                    .SaveAsync(legacy, 0, [], Ct)
            ).Value.ShouldBeTrue();
            var provider = scope
                .ServiceProvider.GetServices<IHotelBookingProvider>()
                .Single(p => p.Key == "duffel-stays");
            ((IHotelLegacyContextConsumer)provider)
                .AcceptsLegacyContext(JsonSerializer.Deserialize<ProviderContextHandle>(context)!)
                .ShouldBeTrue();
        }
        await fixture.RestartAsync();
        using var cold = fixture.Services.CreateScope();
        var service = cold.ServiceProvider.GetRequiredService<HotelReservationService>();
        var detail = (await service.DetailAsync(Owner, legacy.Id, Ct)).Value;
        detail.Outcome.ShouldBe(ReservationOutcome.Unknown);
        detail.Source.ShouldBeNull();
        detail.AcceptedPrice.ShouldBeNull();
        detail.AcceptedConditions.ShouldBeNull();
        (await Buy(fixture, legacy.Id, purchase)).Value.AttemptId.ShouldBe(legacy.AttemptId);
        var actual = legacy.Terms! with { RateId = "different-owned-stage" };
        var updated = await service.ApplyAsync(
            legacy.Id,
            new(ObservationKind.Confirmed, new("bok_legacy_owned", null, actual)),
            true,
            Ct
        );
        updated.Value.ReviewReasons.ShouldNotContain(ReviewReason.TermsMismatch);
        JsonSerializer.Serialize((await Row(fixture, legacy.Id)).Terms).ShouldBe(terms);
        fixture.DuffelWire.Creates.ShouldBe(0);
        fixture.HotelbedsWire.Creates.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Killed_owned_process_after_durable_claim_or_supplier_effect_never_resends(
        bool afterEffect
    )
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        var purchase = Purchase();
        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var configuration =
            typeof(HotelsM2RelationalTests)
                .Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()
                ?.Configuration
            ?? throw new InvalidOperationException("Test assembly configuration is unavailable.");
        configuration.ShouldBeOneOf("Debug", "Release");
        var executable = Path.Combine(
            root.FullName,
            "tools/demo/Travel.Hotels.Fixture/bin",
            configuration,
            "net10.0/Travel.Hotels.Fixture.dll"
        );
        File.Exists(executable)
            .ShouldBeTrue(
                $"Build the owned Travel.Hotels.Fixture project in {configuration} before process-crash acceptance."
            );
        var marker = Path.Combine(fixture.TemporaryDirectory, "crash-boundary.json");
        var manifest = Path.Combine(fixture.TemporaryDirectory, "owned-child.json");
        await File.WriteAllTextAsync(
            manifest,
            JsonSerializer.Serialize(
                new HotelsM2CrashManifest(
                    fixture.Connection,
                    fixture.PrivacyOptions,
                    id,
                    purchase,
                    marker,
                    afterEffect,
                    fixture.HotelbedsWire.ExportRateScopes()
                )
            ),
            Ct
        );
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.FullName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(executable);
        start.ArgumentList.Add("m2-crash-child");
        start.ArgumentList.Add(manifest);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync(Ct);
        var stderr = child.StandardError.ReadToEndAsync(Ct);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (!File.Exists(marker) && !child.HasExited && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(50, Ct);
            File.Exists(marker)
                .ShouldBeTrue(
                    child.HasExited
                        ? "Owned child exited before boundary: " + await stderr
                        : "Owned child did not reach bounded crash boundary."
                );
            using var reached = JsonDocument.Parse(await File.ReadAllTextAsync(marker, Ct));
            reached.RootElement.GetProperty("pid").GetInt32().ShouldBe(child.Id);
            reached
                .RootElement.GetProperty("boundary")
                .GetString()
                .ShouldBe(afterEffect ? "supplier-effect" : "committed-claim-before-send");
            reached.RootElement.GetProperty("source").GetString().ShouldBe("hotelbeds");
            reached.RootElement.GetProperty("creates").GetInt32().ShouldBe(afterEffect ? 1 : 0);
            TestContext.Current.TestOutputHelper?.WriteLine(reached.RootElement.GetRawText());
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Owned child PID {child.Id}; killed at {(afterEffect ? "supplier effect" : "committed claim")}"
            );
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(Ct);
            var spent = await Row(fixture, id);
            spent.AttemptId.ShouldNotBeNull();
            spent.SenderFinished.ShouldBeFalse();
            spent.PrivatePurgeAt.ShouldBeNull();
            await fixture.RestartAsync();
            (await Buy(fixture, id, purchase)).Value.AttemptId.ShouldBe(spent.AttemptId);
            (
                await Buy(fixture, id, purchase with { ActionId = Guid.NewGuid() })
            ).FirstError.ShouldBe(HotelErrors.Conflict);
            await Read(fixture, id);
            fixture.HotelbedsWire.Creates.ShouldBe(0); // The durable external effect marker survives the killed sender separately.
            fixture.HotelbedsWire.Reads.ShouldBe(0);
            (await Row(fixture, id)).AttemptId.ShouldBe(spent.AttemptId);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(Ct);
            }
            await Task.WhenAll(stdout, stderr);
            File.Delete(manifest);
        }
    }

    [Theory]
    [InlineData("hotelbeds")]
    [InlineData("duffel-stays")]
    public async Task Sql_contenders_create_exactly_once_and_cold_replay_keeps_selected_authority(
        string provider
    )
    {
        var gate = new AdmissionSaveGate();
        var capacity = new ContenderCapacity();
        await using var fixture = await Start(configure: services =>
        {
            services.RemoveAll<IHotelCapacity>();
            services.AddSingleton<IHotelCapacity>(capacity);
            services.RemoveAll<IHotelReservationStore>();
            services.AddScoped<IHotelReservationStore>(s => new ContenderStore(
                s.GetRequiredService<HotelReservationStore>(),
                gate
            ));
        });
        var id = await Quote(fixture, provider);
        var purchase = Purchase();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => Buy(fixture, id, purchase))
        );
        responses.ShouldContain(r => !r.IsError);
        gate.ExpectedVersions.Count.ShouldBe(2);
        gate.ExpectedVersions.Distinct().Count().ShouldBe(1);
        gate.LostSaves.ShouldBe(1);
        capacity.Active.ShouldBe(0);
        Creates(fixture, provider).ShouldBe(1);
        Creates(fixture, Other(provider)).ShouldBe(0);
        var before = await Row(fixture, id);
        before.AttemptId.ShouldNotBeNull();
        before.State.AdmissionBinding!.Source.Provider.ShouldBe(provider);
        before.ProtectedParty!.ShouldNotContain("fictional@example.invalid");
        await fixture.RestartAsync();
        (await Buy(fixture, id, purchase)).Value.AttemptId.ShouldBe(before.AttemptId);
        (await Buy(fixture, id, purchase with { ActionId = Guid.NewGuid() })).FirstError.ShouldBe(
            HotelErrors.Conflict
        );
        var changed = purchase with
        {
            Party = purchase.Party! with { Email = "changed@example.invalid" },
        };
        (await Buy(fixture, id, changed)).FirstError.ShouldBe(HotelErrors.Conflict);
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        var other = await Selection(fixture, Other(provider));
        (await service.QuoteAsync(Owner, id, 1, other, Ct)).FirstError.ShouldBe(
            HotelErrors.Conflict
        );
        Creates(fixture, provider).ShouldBe(1);
        Creates(fixture, Other(provider)).ShouldBe(0);
        (await Row(fixture, id)).State.AdmissionBinding.ShouldBe(before.State.AdmissionBinding);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("binding")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("source")]
    [InlineData("context")]
    public async Task Refusal_before_admission_leaves_no_sql_claim_or_supplier_effect(string fault)
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        using var scope = fixture.Services.CreateScope();
        IDisposable? permit = null;
        if (fault == "capacity")
            permit = fixture.Services.GetRequiredService<IHotelCapacity>().TryAcquire("hotelbeds");
        else if (fault == "account")
            fixture.Services.GetRequiredService<IOptions<HotelbedsOptions>>().Value.ApiKey =
                "changed-owned-account-key";
        else
        {
            var store = scope.ServiceProvider.GetRequiredService<IHotelReservationStore>();
            var row = (await store.LoadAsync(id, Owner, Ct)).Value!;
            if (fault == "binding")
                row.State.AdmissionBinding = null;
            if (fault == "environment")
                row.Environment = "WrongEnvironment";
            if (fault == "source")
                row.Provider = "duffel-stays";
            if (fault == "context")
                row.ProtectedContext = row.ProtectedContext![..^3] + "xxx";
            (await store.SaveAsync(row, row.Version, [], Ct)).Value.ShouldBeTrue();
        }
        try
        {
            (await Buy(fixture, id, Purchase())).IsError.ShouldBeTrue();
        }
        finally
        {
            permit?.Dispose();
        }
        (await Row(fixture, id)).AttemptId.ShouldBeNull();
        fixture.HotelbedsWire.Creates.ShouldBe(0);
        fixture.DuffelWire.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Supplier_effect_with_lost_reply_and_cold_Host_never_resends_or_discovers()
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        fixture.HotelbedsWire.Unknown = true;
        var purchase = Purchase();
        (await Buy(fixture, id, purchase)).Value.Outcome.ShouldBe(ReservationOutcome.Unknown);
        var spent = await Row(fixture, id);
        spent.PrivatePurgeAt.ShouldBeNull();
        await fixture.RestartAsync();
        (await Buy(fixture, id, purchase)).Value.AttemptId.ShouldBe(spent.AttemptId);
        (
            await Buy(fixture, id, purchase with { ActionId = Guid.NewGuid() })
        ).IsError.ShouldBeTrue();
        await Read(fixture, id);
        fixture.HotelbedsWire.Creates.ShouldBe(1);
        fixture.HotelbedsWire.Reads.ShouldBe(0);
        fixture.DuffelWire.Creates.ShouldBe(0);
        using var cleanup = fixture.Services.CreateScope();
        (
            await cleanup
                .ServiceProvider.GetRequiredService<HotelReservationStore>()
                .PurgePrivateContentAsync(DateTimeOffset.UtcNow.AddYears(10), Ct)
        ).ShouldBe(0);
        (await Row(fixture, id)).ProtectedContext.ShouldNotBeNull();
    }

    [Fact]
    public async Task Known_reference_three_reads_and_five_minute_window_survive_cold_Host_and_duplicate_work()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        await using var fixture = await Start(time);
        var id = await Quote(fixture, "hotelbeds");
        fixture.HotelbedsWire.ReservedWithoutDetails = true;
        fixture.HotelbedsWire.ReadsUnavailable = true;
        (await Buy(fixture, id, Purchase())).IsError.ShouldBeFalse();
        await Read(fixture, id);
        var first = await Row(fixture, id);
        first.State.ReadsClaimed.ShouldBe(1);
        var until = first.State.ReadUntil;
        await fixture.RestartAsync();
        await Read(fixture, id);
        fixture.HotelbedsWire.Reads.ShouldBe(1);
        time.Advance(TimeSpan.FromSeconds(31));
        await Read(fixture, id);
        time.Advance(TimeSpan.FromSeconds(121));
        await Read(fixture, id);
        await fixture.RestartAsync();
        await Read(fixture, id);
        fixture.HotelbedsWire.Reads.ShouldBe(3);
        var saved = await Row(fixture, id);
        saved.State.ReadsClaimed.ShouldBe(3);
        saved.State.ReadUntil.ShouldBe(until);
        saved.State.EverReserved.ShouldBeTrue();
        time.Advance(TimeSpan.FromMinutes(5));
        using (var scope = fixture.Services.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<HotelReservationService>()
                    .ApplyAsync(
                        id,
                        new(
                            ObservationKind.ReservedWithoutDetails,
                            new(saved.State.BoundBookingId!, null, null)
                        ),
                        false,
                        Ct
                    )
            ).IsError.ShouldBeFalse();
        await Read(fixture, id);
        fixture.HotelbedsWire.Reads.ShouldBe(3);
        (await Row(fixture, id)).State.ReadUntil.ShouldBe(until);
        fixture.HotelbedsWire.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task Actual_Hotelbeds_read_mismatch_is_retained_after_cold_Host_without_compensating_effect()
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        fixture.HotelbedsWire.ReservedWithoutDetails = true;
        fixture.HotelbedsWire.ReadTotal = 999m;
        (await Buy(fixture, id, Purchase())).IsError.ShouldBeFalse();
        await fixture.RestartAsync();
        await Read(fixture, id);
        var saved = await Row(fixture, id);
        saved.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        saved.State.EverReserved.ShouldBeTrue();
        saved.State.Review.ShouldContain(ReviewReason.TermsMismatch);
        saved.State.FirstMismatch!.Actual.Total.Amount.ShouldBe(999m);
        fixture.HotelbedsWire.Creates.ShouldBe(1);
        fixture.HotelbedsWire.Reads.ShouldBe(1);
        fixture.DuffelWire.Creates.ShouldBe(0);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("profile")]
    [InlineData("environment")]
    public async Task Reader_binding_change_is_persisted_review_with_zero_wrong_HTTP(
        string mutation
    )
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        // Prevent automatic first read by occupying only the recovery lane.
        using var permit = fixture
            .Services.GetRequiredService<IHotelCapacity>()
            .TryAcquire("hotelbeds", true);
        fixture.HotelbedsWire.ReservedWithoutDetails = true;
        (await Buy(fixture, id, Purchase())).IsError.ShouldBeFalse();
        var options = fixture.Services.GetRequiredService<IOptions<HotelbedsOptions>>().Value;
        if (mutation == "account")
            options.ApiKey = "changed-owned-account-key";
        if (mutation == "profile")
            options.Fictional = false;
        if (mutation == "environment")
        {
            using var scope = fixture.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IHotelReservationStore>();
            var row = (await store.LoadAsync(id, Owner, Ct)).Value!;
            var binding = row.State.AdmissionBinding!;
            row.State.AdmissionBinding = binding with
            {
                Source = binding.Source with { Environment = "Changed" },
            };
            (await store.SaveAsync(row, row.Version, [], Ct)).Value.ShouldBeTrue();
        }
        await Read(fixture, id);
        fixture.HotelbedsWire.Reads.ShouldBe(0);
        var saved = await Row(fixture, id);
        saved.State.ReadsClaimed.ShouldBe(0);
        saved.State.Review.ShouldContain(ReviewReason.ObservationUnavailable);
        saved.AttemptId.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("hotelbeds")]
    [InlineData("duffel-stays")]
    public async Task Global_M2_off_refuses_new_v2_admission_but_matching_admitted_reader_drains(
        string source
    )
    {
        await using var fixture = await Start();
        var draft = await Quote(fixture, source);
        var options = fixture.Services.GetRequiredService<IOptions<HotelsOptions>>().Value;
        options.Aggregation.Enabled = false;
        (await Buy(fixture, draft, Purchase())).IsError.ShouldBeTrue();
        (await Row(fixture, draft)).AttemptId.ShouldBeNull();
        options.Aggregation.Enabled = true;
        fixture.HotelbedsWire.ReservedWithoutDetails = true;
        fixture.DuffelWire.ReservedWithoutDetails = true;
        (await Buy(fixture, draft, Purchase())).IsError.ShouldBeFalse();
        options.Aggregation.Enabled = false;
        options.BookingEnabled = false;
        fixture.Services.GetRequiredService<IOptions<HotelbedsOptions>>().Value.BookingEnabled =
            false;
        await Read(fixture, draft);
        (await Row(fixture, draft)).State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        Creates(fixture, source).ShouldBe(1);
        Reads(fixture, source).ShouldBe(1);
    }

    [Theory]
    [InlineData("certificate")]
    [InlineData("ring")]
    public async Task Physical_cold_key_outage_keeps_metadata_local_and_refuses_new_claim(
        string missing
    )
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        var original = Path.Combine(
            fixture.TemporaryDirectory,
            missing == "ring" ? "ring" : "fictional.pfx"
        );
        var moved = original + ".owned-offline";
        var ownedRoot = Path.GetFullPath(fixture.TemporaryDirectory) + Path.DirectorySeparatorChar;
        Path.GetFullPath(original)
            .StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue();
        Path.GetFullPath(moved)
            .StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue();
        if (missing == "ring")
            Directory.Move(original, moved);
        else
            File.Move(original, moved);
        try
        {
            await fixture.RestartAsync();
            fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                "fictional-hotels-owner"
            );
            foreach (
                var path in new[] { "/api/hotels/reservations", $"/api/hotels/reservations/{id}" }
            )
            {
                using var response = await fixture.Client.GetAsync(path, Ct);
                response.EnsureSuccessStatusCode();
                response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            }
            (await Buy(fixture, id, Purchase())).IsError.ShouldBeTrue();
            (await Row(fixture, id)).AttemptId.ShouldBeNull();
            fixture.HotelbedsWire.Creates.ShouldBe(0);
            fixture.HotelbedsWire.Reads.ShouldBe(0);
        }
        finally
        {
            if (missing == "ring")
                Directory.Move(moved, original);
            else
                File.Move(moved, original);
        }
    }

    [Fact]
    public async Task Real_envelope_binding_owner_check_and_sql_retention_remove_new_private_facts_but_keep_authority()
    {
        await using var fixture = await Start();
        var id = await Quote(fixture, "hotelbeds");
        var purchase = Purchase();
        (await Buy(fixture, id, purchase)).IsError.ShouldBeFalse();
        var saved = await Row(fixture, id);
        saved.State.AcceptedPrice.ShouldNotBeNull();
        saved.State.AcceptedConditions.ShouldNotBeNull();
        saved.PrivatePurgeAt.ShouldNotBeNull();
        var checkout = new DateTimeOffset(
            saved.Terms!.Dates.CheckOut.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero
        );
        saved.PrivatePurgeAt.ShouldBe(
            (checkout > saved.UpdatedAt ? checkout : saved.UpdatedAt).AddDays(90)
        );
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HotelReservationService>();
        var protector = scope.ServiceProvider.GetRequiredService<IHotelPrivateProtector>();
        foreach (var purpose in new[] { "context", "party" })
        {
            var envelope = purpose == "context" ? saved.ProtectedContext! : saved.ProtectedParty!;
            var binding = HotelReservationService.Binding(saved, purpose);
            protector
                .Unprotect(envelope, binding with { OwnerId = Guid.NewGuid() })
                .IsError.ShouldBeTrue();
            protector
                .Unprotect(envelope, binding with { QuoteRevision = 2 })
                .IsError.ShouldBeTrue();
            protector
                .Unprotect(
                    envelope,
                    binding with
                    {
                        Kind = purpose == "context" ? "party" : "context",
                    }
                )
                .IsError.ShouldBeTrue();
            protector.Unprotect(envelope[..^3] + "xxx", binding).IsError.ShouldBeTrue();
        }
        // Invalid encrypted body must not be touched for an unauthorized owner.
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        await db
            .Reservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.ProtectedParty, "invalid-owned-envelope"),
                Ct
            );
        (await service.PurchaseAsync(Guid.NewGuid(), id, purchase, Ct)).FirstError.ShouldBe(
            HotelErrors.Missing
        );
        var actual = saved.Terms! with { Room = "Owned mismatch" };
        (
            await service.ApplyAsync(
                id,
                new(ObservationKind.Confirmed, new(saved.State.BoundBookingId!, null, actual)),
                false,
                Ct
            )
        ).IsError.ShouldBeFalse();
        await db
            .Reservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(r => r.PrivatePurgeAt, DateTimeOffset.UtcNow.AddSeconds(-1))
                        .SetProperty(r => r.Version, r => r.Version + 1),
                Ct
            );
        (
            await scope
                .ServiceProvider.GetRequiredService<HotelReservationStore>()
                .PurgePrivateContentAsync(DateTimeOffset.UtcNow, Ct)
        ).ShouldBe(1);
        var purged = await Row(fixture, id);
        purged.Terms.ShouldBeNull();
        purged.ProtectedContext.ShouldBeNull();
        purged.ProtectedParty.ShouldBeNull();
        purged.State.AcceptedPrice.ShouldBeNull();
        purged.State.AcceptedConditions.ShouldBeNull();
        purged.State.FirstMismatch.ShouldBeNull();
        purged.State.Facts!.Actual.ShouldBeNull();
        purged.AttemptId.ShouldBe(saved.AttemptId);
        purged.State.AdmissionBinding.ShouldBe(saved.State.AdmissionBinding);
        (await Buy(fixture, id, purchase)).FirstError.ShouldBe(HotelErrors.Conflict);
        fixture.HotelbedsWire.Creates.ShouldBe(1);
    }

    private static async Task<HotelsHostFixture> Start(
        TimeProvider? clock = null,
        Action<IServiceCollection>? configure = null
    )
    {
        // Future controlled time keeps durable scheduled work available for explicit duplicate-delivery tests.
        var fixture = new HotelsHostFixture
        {
            Multisupplier = true,
            Clock = clock ?? new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1)),
        };
        fixture.ConfigureTestServices = configure;
        fixture.AssetsReady = value =>
            TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(value));
        await fixture.StartAsync(Ct);
        return fixture;
    }

    private static async Task<SelectedStay> Selection(HotelsHostFixture fixture, string source)
    {
        var provider = fixture
            .Services.GetServices<IHotelSearchProvider>()
            .Single(p => p.Key == source);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        var request = new StaySearch(
            new("fictional", 0, 0, 1, true),
            new(date, date.AddDays(2)),
            new(1)
        );
        var result = await provider.SearchAsync(request, Ct);
        result.Items.Length.ShouldBe(1);
        var rates = await provider.LoadRatesAsync(new(request, result.Items[0]), Ct);
        rates.Items.Length.ShouldBe(1);
        return new(request, rates.Items[0]);
    }

    private static async Task<Guid> Quote(HotelsHostFixture fixture, string source)
    {
        var selected = await Selection(fixture, source);
        using var scope = fixture.Services.CreateScope();
        var id = Guid.NewGuid();
        var result = await scope
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .QuoteAsync(Owner, id, null, selected, Ct);
        result.IsError.ShouldBeFalse(result.IsError ? result.FirstError.Code : "");
        return id;
    }

    private static HotelPurchase Purchase() =>
        new(
            Guid.NewGuid(),
            1,
            true,
            new(
                [new("Fictional", "Traveller")],
                "fictional@example.invalid",
                "+44000000000",
                true,
                true
            )
        );

    private static async Task<ErrorOr<HotelReservationView>> Buy(
        HotelsHostFixture fixture,
        Guid id,
        HotelPurchase purchase
    )
    {
        using var scope = fixture.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .PurchaseAsync(Owner, id, purchase, Ct);
    }

    private static async Task<HotelReservation> Row(HotelsHostFixture fixture, Guid id)
    {
        using var scope = fixture.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .LoadAsync(id, null, Ct)
        ).Value!;
    }

    private static async Task Read(HotelsHostFixture fixture, Guid id)
    {
        using var scope = fixture.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<HotelObservationRunner>()
            .RunAsync(new(id, false, fixture.Clock.GetUtcNow()), Ct);
    }

    private static int Creates(HotelsHostFixture f, string source) =>
        source == "hotelbeds" ? f.HotelbedsWire.Creates : f.DuffelWire.Creates;

    private static int Reads(HotelsHostFixture f, string source) =>
        source == "hotelbeds" ? f.HotelbedsWire.Reads : f.DuffelWire.Reads;

    private static string Other(string source) =>
        source == "hotelbeds" ? "duffel-stays" : "hotelbeds";

    private sealed class AdmissionSaveGate
    {
        private int entered;
        private int lost;
        private readonly TaskCompletionSource ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public System.Collections.Concurrent.ConcurrentBag<int> ExpectedVersions { get; } = [];
        public int LostSaves => lost;

        public async Task BeforeSave(int version, CancellationToken ct)
        {
            ExpectedVersions.Add(version);
            if (Interlocked.Increment(ref entered) == 2)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        public void Lost() => Interlocked.Increment(ref lost);
    }

    private sealed class ContenderStore(HotelReservationStore inner, AdmissionSaveGate gate)
        : IHotelReservationStore
    {
        public Task<ErrorOr<HotelReservation?>> LoadAsync(
            Guid id,
            Guid? owner,
            CancellationToken ct
        ) => inner.LoadAsync(id, owner, ct);

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            inner.ListAsync(owner, ct);

        public async Task<ErrorOr<bool>> SaveAsync(
            HotelReservation row,
            int version,
            HotelWork[] work,
            CancellationToken ct
        )
        {
            var admission = row.AttemptId is not null && work.Any(w => w.Escalation);
            if (admission)
                await gate.BeforeSave(version, ct);
            var result = await inner.SaveAsync(row, version, work, ct);
            if (admission && !result.IsError && !result.Value)
                gate.Lost();
            return result;
        }
    }

    private sealed class ContenderCapacity : IHotelCapacity
    {
        private int active;
        public int Active => active;

        public IDisposable TryAcquire(string provider, bool recovery = false)
        {
            Interlocked.Increment(ref active);
            return new Lease(() => Interlocked.Decrement(ref active));
        }

        private sealed class Lease(Action release) : IDisposable
        {
            public void Dispose() => release();
        }
    }
}
