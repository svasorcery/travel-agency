extern alias TravelAiApp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using AiLedger = TravelAiApp::Travel.AI.TripsBriefExtraction.ITripsBriefLedger;
using AiOptions = TravelAiApp::Travel.AI.TripsBriefExtraction.TripsBriefOptions;
using ProductionAiLedger = TravelAiApp::Travel.AI.TripsBriefExtraction.TripsBriefLedger;

namespace Travel.Host.Tests.Integration.NlSearch;

// Opt-in extension only: real Programs, initialization, NATS and SQL; only the keyed M3 model is fictional.
public sealed partial class NlSearchTransportFixture
{
    public const string BriefModel = "trips-transport-model";
    public const string BriefIssuer = "https://fictional.trips-brief.test/realms/travel";
    public static string BriefPolicy =>
        TripsBriefPolicyIdentity.Compute(
            "anthropic",
            BriefModel,
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
    private readonly bool _briefEnabled;
    private readonly bool _briefFeatureEnabled;
    private readonly BriefFixtureLogs _briefLogs = new();
    private readonly BriefPipelineChat _briefOne = new();
    private readonly BriefPipelineChat _briefTwo = new();

    public HttpClient CreateBriefHttpClient() => _host!.Server.CreateClient();

    public int BriefCalls => _briefOne.Calls + _briefTwo.Calls;
    public string[] BriefLogs => _briefLogs.Messages.ToArray();

    public void SetBriefMode(string mode, string marker)
    {
        _briefOne.Mode = mode;
        _briefTwo.Mode = mode;
        _briefOne.Marker = marker;
        _briefTwo.Marker = marker;
    }

    private Dictionary<string, string?> BriefSettings(string connection, string nats, bool ai) =>
        new()
        {
            ["environment"] = "Testing",
            ["ConnectionStrings:travel"] = connection,
            ["ConnectionStrings:nats"] = nats,
            ["ConnectionStrings:redis"] = "",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
            ["HealthEndpoints:InternalPort"] = ai ? "5159" : "5098",
            ["Keycloak:Authority"] = "",
            ["Keycloak:Audience"] = "travel-web",
            ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "false",
            ["Flights:FeatureFlags:NlSearch:Enabled"] = "false",
            ["Flights:Duffel:BaseUrl"] = "http://127.0.0.1:1",
            ["Flights:Providers:Frankfurter:BaseAddress"] = "http://127.0.0.1:1",
            ["Trips:Enabled"] = "true",
            ["Trips:Itinerary:Enabled"] = "false",
            ["Trips:BriefExtraction:Enabled"] = _briefFeatureEnabled.ToString(),
            ["Trips:BriefExtraction:ProcessorId"] = "anthropic",
            ["Trips:BriefExtraction:ModelId"] = BriefModel,
            ["Trips:BriefExtraction:PrivacyNoticeVersion"] =
                TripsBriefMessageIdentity.PrivacyNoticeVersion,
            ["TripsBrief:Enabled"] = _briefFeatureEnabled.ToString(),
            ["TripsBrief:ProcessorId"] = "anthropic",
            ["TripsBrief:Model"] = BriefModel,
            ["TripsBrief:PricingModel"] = BriefModel,
            ["TripsBrief:PricingVerified"] = "true",
            ["TripsBrief:InputUsdPerMillion"] = "1",
            ["TripsBrief:OutputUsdPerMillion"] = "1",
            ["TripsBrief:ExecutionAuthorized"] = "true",
            ["TripsBrief:PrivateTextProcessingAuthorized"] = "true",
            ["TripsBrief:MaxRequestCostUsd"] = "0.03",
            ["TripsBrief:MaxHourlyCostUsd"] = "0.30",
        };

    private void ConfigureBriefBuilder(
        IWebHostBuilder builder,
        string connection,
        string nats,
        bool ai
    )
    {
        builder.UseEnvironment("Testing");
        foreach (var setting in BriefSettings(connection, nats, ai))
            builder.UseSetting(setting.Key, setting.Value);
        builder.ConfigureAppConfiguration(
            (context, configuration) =>
            {
                if (
                    !context.HostingEnvironment.IsEnvironment("Testing")
                    || configuration
                        .Sources.OfType<JsonConfigurationSource>()
                        .Any(s =>
                            string.Equals(
                                Path.GetFileName(s.Path),
                                "secrets.json",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                )
                    throw new InvalidOperationException(
                        "Saved-secret configuration cannot enter the M3 fixture."
                    );
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(BriefSettings(connection, nats, ai));
            }
        );
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(_briefLogs);
        });
    }

    private void ConfigureBriefAi(
        IWebHostBuilder builder,
        string replicaModel,
        string connection,
        string nats
    )
    {
        ConfigureBriefBuilder(builder, connection, nats, true);
        builder.ConfigureServices(services =>
        {
            services.RemoveAllKeyed<IChatClient>(AiOptions.ChatClientKey);
            services.AddKeyedSingleton<IChatClient>(
                AiOptions.ChatClientKey,
                replicaModel == DeterministicChatClient.ReplicaOneModelId ? _briefOne : _briefTwo
            );
        });
    }

    private void ConfigureBriefHost(IWebHostBuilder builder, string connection, string nats)
    {
        ConfigureBriefBuilder(builder, connection, nats, false);
        builder.ConfigureServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TripsBriefFictional";
                    options.DefaultChallengeScheme = "TripsBriefFictional";
                })
                .AddScheme<AuthenticationSchemeOptions, BriefFixtureAuthentication>(
                    "TripsBriefFictional",
                    _ => { }
                );
        });
        var activities = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
                _briefLogs.Messages.Enqueue(
                    activity.DisplayName
                        + " "
                        + activity.StatusDescription
                        + " "
                        + string.Join(
                            " ",
                            activity.TagObjects.Select(t =>
                                $"{t.Key}={BriefFixtureLogs.RenderValue(t.Value)}"
                            )
                        )
                        + " "
                        + string.Join(
                            " ",
                            activity.Events.Select(e =>
                                e.Name
                                + " "
                                + string.Join(
                                    " ",
                                    e.Tags.Select(t =>
                                        $"{t.Key}={BriefFixtureLogs.RenderValue(t.Value)}"
                                    )
                                )
                            )
                        )
                ),
        };
        ActivitySource.AddActivityListener(activities);
        _lifecycle.RegisterCleanup(
            "M3 activity capture",
            _ =>
            {
                activities.Dispose();
                return ValueTask.CompletedTask;
            }
        );
    }

    public async Task<NatsListenerMembership> WaitForBriefMembershipAsync(
        int expected,
        CancellationToken ct
    )
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(ListenerMembershipTimeout);
        string last = "none";
        try
        {
            while (true)
            {
                var json = await _natsMonitorClient.GetStringAsync(
                    "subsz?subs=1&test=" + TripsBriefMessageIdentity.Subject + "&limit=1024",
                    bound.Token
                );
                last = json;
                if (json.Length > MaxNatsMonitoringResponseCharacters)
                    throw new InvalidOperationException("M3 NATS diagnostic bounds exceeded.");
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var subscriptions = root.TryGetProperty("subscriptions_list", out var list)
                    ? list.EnumerateArray().ToArray()
                    : [];
                var matches = subscriptions
                    .Where(s =>
                        s.GetProperty("subject").GetString() == TripsBriefMessageIdentity.Subject
                    )
                    .ToArray();
                if (
                    matches.Length == expected
                    && matches.All(s =>
                        s.TryGetProperty("qgroup", out var group)
                        && group.GetString() == TripsBriefMessageIdentity.QueueGroup
                    )
                    && matches.Select(s => s.GetProperty("cid").GetInt64()).Distinct().Count()
                        == expected
                )
                    return new(
                        TripsBriefMessageIdentity.Subject,
                        TripsBriefMessageIdentity.QueueGroup,
                        expected,
                        expected
                    );
                await Task.Delay(ListenerMembershipPollInterval, bound.Token);
            }
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            var flags = new[] { _aiReplicaOneHost, _aiReplicaTwoHost }
                .Where(h => h is not null)
                .Select(h =>
                {
                    var o = h!.Services.GetRequiredService<AiOptions>();
                    return new
                    {
                        o.Enabled,
                        o.IsConfigured,
                        o.Model,
                    };
                })
                .ToArray();
            throw new TimeoutException(
                "M3 listener membership not established: "
                    + JsonSerializer.Serialize(flags)
                    + "; snapshot="
                    + last
            );
        }
    }

    public async Task<(
        string Subject,
        string[] Members,
        string[] HeaderNames
    )> ObserveBriefEnvelopeAsync(CancellationToken ct)
    {
        while (true)
        {
            var message = await _observedMessages!.Msgs.ReadAsync(ct);
            if (message.Subject != TripsBriefMessageIdentity.Subject)
                continue;
            using var doc = JsonDocument.Parse(message.Data!);
            return (
                message.Subject,
                doc.RootElement.EnumerateObject()
                    .Select(p => p.Name.ToLowerInvariant())
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                message
                    .Headers?.Select(h => h.Key.ToLowerInvariant())
                    .Order(StringComparer.Ordinal)
                    .ToArray()
                    ?? []
            );
        }
    }

    public async Task<BriefLedgerEvidence[]> ReadBriefLedgerAsync(
        Guid requestId,
        CancellationToken ct
    )
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "select outcome, input_tokens, output_tokens, cost_usd, model, user_id from ai.cost_ledger where correlation_id=@id and feature='trips.brief_extraction'",
            connection
        );
        command.Parameters.AddWithValue("id", requestId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<BriefLedgerEvidence>();
        while (await reader.ReadAsync(ct))
            rows.Add(
                new(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5)
                )
            );
        return rows.ToArray();
    }

    public async Task AssertBriefProductionCompositionAsync(CancellationToken ct)
    {
        foreach (var host in new[] { _aiReplicaOneHost, _aiReplicaTwoHost })
        {
            await using var scope = host!.Services.CreateAsyncScope();
            if (
                scope.ServiceProvider.GetRequiredService<AiLedger>() is not ProductionAiLedger
                || scope.ServiceProvider.GetRequiredService<IChatClient>()
                    is not TravelAiApp::Travel.AI.NlSearch.NlSearchChatClient
            )
                throw new InvalidOperationException(
                    "M3 fixture must retain production EF ledger and unrelated Flights client."
                );
        }
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema='trips'",
            connection
        );
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException(
                "Extraction independence requires no provisioned M1 Trips schema."
            );
    }

    // Audit all actual durable envelope writes, including rows deleted before a post-request scan.
    // The audit stores public table metadata and a boolean only, never the private payload or marker.
    public async Task InstallBriefDurableAuditAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(ct);
        const string setup = """
            create schema m3_fixture_audit;
            create table m3_fixture_audit.writes(target text not null, matched boolean not null);
            create function m3_fixture_audit.record_write() returns trigger language plpgsql as $$
            declare serialized text := to_jsonb(new)::text;
            begin
                insert into m3_fixture_audit.writes(target,matched) values(
                    tg_table_schema || '.' || tg_table_name,
                    serialized ~* '(trips[-.]?brief|74726176656c2e61692e74726970732d6272696566|54726970734272696566)');
                return new;
            end $$;
            create table m3_fixture_audit.probe(message_type text, data bytea);
            create trigger trips_m3_probe after insert or update on m3_fixture_audit.probe for each row execute function m3_fixture_audit.record_write();
            insert into m3_fixture_audit.probe(message_type) values('travel.ai.trips-brief.requested');
            insert into m3_fixture_audit.probe(data) values(convert_to('travel.ai.trips-brief.parsed','UTF8'));
            """;
        await using (var command = new NpgsqlCommand(setup, connection))
            await command.ExecuteNonQueryAsync(ct);
        await using (
            var command = new NpgsqlCommand(
                "select count(*) from m3_fixture_audit.writes where matched",
                connection
            )
        )
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 2)
                throw new InvalidOperationException("M3 durable audit positive controls failed.");
        await using (
            var command = new NpgsqlCommand(
                "truncate m3_fixture_audit.writes; drop table m3_fixture_audit.probe",
                connection
            )
        )
            await command.ExecuteNonQueryAsync(ct);
        var tables = new List<(string Schema, string Name)>();
        await using (
            var command = new NpgsqlCommand(
                "select table_schema,table_name from information_schema.tables where table_type='BASE TABLE' and table_name like 'wolverine_%' and (table_name like '%incoming%' or table_name like '%outgoing%' or table_name like '%dead%')",
                connection
            )
        )
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                tables.Add((reader.GetString(0), reader.GetString(1)));
        if (tables.Count < 3)
            throw new InvalidOperationException(
                "M3 privacy proof requires the real inbox/outbox/deadletter tables."
            );
        foreach (var (schema, name) in tables)
        {
            var quoted =
                '"' + schema.Replace("\"", "\"\"") + "\".\"" + name.Replace("\"", "\"\"") + '"';
            await using var command = new NpgsqlCommand(
                $"create trigger trips_m3_audit after insert or update on {quoted} for each row execute function m3_fixture_audit.record_write()",
                connection
            );
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<string[]> AssertBriefDurablePrivacyAsync(string marker, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(ct);
        await using (
            var audit = new NpgsqlCommand(
                "select count(*) from m3_fixture_audit.writes where matched",
                connection
            )
        )
            if (Convert.ToInt32(await audit.ExecuteScalarAsync(ct)) != 0)
                throw new InvalidOperationException(
                    "M3 message was written to durable envelopes, including a row later deleted."
                );
        var names = new List<(string Schema, string Name)>();
        await using (
            var command = new NpgsqlCommand(
                "select table_schema, table_name from information_schema.tables where table_type='BASE TABLE' and table_schema not in ('pg_catalog','information_schema') order by table_schema,table_name",
                connection
            )
        )
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                names.Add((reader.GetString(0), reader.GetString(1)));
        var needles = new[]
        {
            marker,
            Convert.ToHexStringLower(Encoding.UTF8.GetBytes(marker)),
            "\"evidence\"",
            Convert.ToHexStringLower(Encoding.UTF8.GetBytes("\"evidence\"")),
            Convert.ToHexStringLower(Encoding.UTF8.GetBytes("\"Evidence\"")),
        };
        foreach (var (schema, name) in names)
        {
            var quoted =
                '"' + schema.Replace("\"", "\"\"") + "\".\"" + name.Replace("\"", "\"\"") + '"';
            await using var command = new NpgsqlCommand(
                $"select to_jsonb(stored_row)::text from {quoted} stored_row limit 1001",
                connection
            );
            await using var reader = await command.ExecuteReaderAsync(ct);
            var count = 0;
            while (await reader.ReadAsync(ct))
            {
                if (++count > 1000)
                    throw new InvalidOperationException(
                        "M3 privacy scan exceeds owned fixture bounds."
                    );
                var row = reader.GetString(0);
                if (needles.Any(n => row.Contains(n, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        "Private M3 payload found in durable table " + schema + "." + name
                    );
            }
        }
        return names.Select(n => n.Schema + "." + n.Name).ToArray();
    }

    public async Task SetBriefLedgerOutageAsync(bool enabled, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            enabled
                ? "alter table ai.cost_ledger add constraint trips_m3_fixture_outage check(feature <> 'trips.brief_extraction') not valid"
                : "alter table ai.cost_ledger drop constraint trips_m3_fixture_outage",
            connection
        );
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RestartOneBriefAiAsync(CancellationToken ct)
    {
        _aiReplicaOneHost = await StartAiReplicaAsync(
            "M3 restarted replica",
            _replicaOneChat,
            _postgres.GetConnectionString(),
            $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(NatsPort)}",
            ct
        );
        await WaitForBriefMembershipAsync(1, ct);
    }

    public sealed record BriefLedgerEvidence(
        string Outcome,
        int? InputTokens,
        int? OutputTokens,
        decimal? CostUsd,
        string Model,
        Guid? UserId
    );

    private sealed class BriefPipelineChat : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public string Mode { get; set; } = "Ready";
        public string Marker { get; set; } = "";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref calls);
            if (Mode == "Exception")
                throw new InvalidOperationException("Fictional SDK payload " + Marker);
            var fields =
                "\"destinationId\":{\"state\":\"extracted\",\"value\":\"moscow\",\"evidence\":[{\"start\":0,\"end\":6}],\"alternatives\":[]},"
                + string.Join(
                    ",",
                    new[]
                    {
                        "startDate",
                        "endDate",
                        "durationDays",
                        "adults",
                        "interests",
                        "pace",
                    }.Select(n =>
                        $"\"{n}\":{{\"state\":\"missing\",\"value\":null,\"evidence\":[],\"alternatives\":[]}}"
                    )
                );
            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(
                        ChatRole.Assistant,
                        Mode == "InvalidOutput"
                            ? Marker
                            : "{\"fields\":{" + fields + "},\"issues\":[]}"
                    )
                )
                {
                    ModelId = Mode == "WrongModel" ? Marker : BriefModel,
                    Usage =
                        Mode == "UsageUnknown"
                            ? null
                            : new() { InputTokenCount = 20, OutputTokenCount = 10 },
                }
            );
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type type, object? key = null) => null;

        public void Dispose() { }
    }

    private sealed class BriefFixtureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(Messages, categoryName);

        public void Dispose() { }

        // Capture structured values as well as their formatter/ToString: a redacting record formatter
        // must not hide a private object retained in a logging attribute, scope or Activity tag.
        private static readonly JsonSerializerOptions DiagnosticJson = new()
        {
            MaxDepth = 16,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };

        public static string RenderValue(object? value)
        {
            if (value is null)
                return "null";
            if (value is string text)
                return text;
            if (value is byte[] data)
                return Encoding.UTF8.GetString(data);
            if (value is ReadOnlyMemory<byte> memory)
                return Encoding.UTF8.GetString(memory.Span);
            if (value is Wolverine.Envelope envelope)
                return "envelope:"
                    + envelope.MessageType
                    + " message="
                    + RenderValue(envelope.Message)
                    + " data="
                    + RenderValue(envelope.Data)
                    + " headers="
                    + RenderValue(envelope.Headers);
            if (value is IEnumerable<KeyValuePair<string, object?>> values)
                return string.Join(" ", values.Select(v => v.Key + "=" + RenderValue(v.Value)));
            try
            {
                return JsonSerializer.Serialize(value, value.GetType(), DiagnosticJson);
            }
            catch (NotSupportedException)
            {
                return "unsupported-metadata:" + value.GetType().FullName + ":" + value;
            }
            catch (JsonException)
            {
                return "unsupported-metadata:" + value.GetType().FullName + ":" + value;
            }
        }

        private sealed class Capture(ConcurrentQueue<string> messages, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                messages.Enqueue(
                    category + " scope " + state + " structured=" + RenderValue(state)
                );
                return null;
            }

            public bool IsEnabled(LogLevel level) => true;

            public void Log<TState>(
                LogLevel level,
                EventId id,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) =>
                messages.Enqueue(
                    category
                        + " event="
                        + id.Id
                        + " "
                        + formatter(state, exception)
                        + " "
                        + exception
                        + " structured="
                        + RenderValue(state)
                );
        }
    }
}

public sealed class BriefFixtureAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Trips-Owner"], out var owner) || owner == Guid.Empty)
            return Task.FromResult(AuthenticateResult.NoResult());
        var issuer = NlSearchTransportFixture.BriefIssuer;
        var claimIssuer =
            Request.Headers["X-Brief-Identity"] == "untrusted" ? "https://foreign.invalid" : issuer;
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", owner.ToString(), ClaimValueTypes.String, claimIssuer),
                new Claim("iss", issuer, ClaimValueTypes.String, issuer),
            ],
            Scheme.Name
        );
        return Task.FromResult(
            AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name))
        );
    }
}
