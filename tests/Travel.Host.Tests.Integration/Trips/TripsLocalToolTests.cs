using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Trips.LocalStack;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

public sealed class TripsLocalToolTests
{
    [Theory]
    [InlineData("mode", "production")]
    [InlineData("projectName", "travel-trips-foreign")]
    [InlineData("id", "../../foreign")]
    [InlineData("applicationAsOfUtc", "2030-01-01T00:00:00Z")]
    [InlineData("dockerEndpoint", "tcp://remote.example:2375")]
    public void Refuses_tampered_owned_state(string field, string value)
    {
        using var fixture = new OwnedDirectory();
        var state = JsonNode.Parse(File.ReadAllText(fixture.StateFile))!;
        state[field] = value;
        File.WriteAllText(fixture.StateFile, state.ToJsonString());
        Should.Throw<LocalStackRefusal>(() =>
            LocalStackState.ReadLocal(fixture.StateDir, fixture.Root)
        );
    }

    [Fact]
    public void Refuses_copied_state_and_wrong_ports_before_provisioning()
    {
        using var fixture = new OwnedDirectory();
        Should.Throw<LocalStackRefusal>(() =>
            LocalStackState.ReadLocal(fixture.StateDir, Path.Combine(fixture.Root, "other"))
        );
        var state = JsonNode.Parse(File.ReadAllText(fixture.StateFile))!;
        state["webPort"] = 4200;
        File.WriteAllText(fixture.StateFile, state.ToJsonString());
        Should.Throw<LocalStackRefusal>(() =>
            LocalStackState.ReadLocal(fixture.StateDir, fixture.Root)
        );
        Directory.Exists(Path.Combine(fixture.StateDir, "private")).ShouldBeFalse();
    }

    [Fact]
    public void Only_fixed_owned_database_configuration_and_exact_marker_are_accepted()
    {
        LocalDatabase.ValidateConnection(
            "Host=postgres;Port=5432;Database=travel;Username=postgres;Password=fictional",
            "fictional"
        );
        foreach (
            var connection in new[]
            {
                "Host=remote;Database=travel;Username=postgres;Password=fictional",
                "Host=postgres;Database=production;Username=postgres;Password=fictional",
                "Host=postgres;Port=5433;Database=travel;Username=postgres;Password=fictional",
                "Host=postgres;Database=travel;Username=postgres;Password=wrong",
                "Host=postgres;Database=travel;Username=postgres;Password=fictional;Search Path=other",
            }
        )
            Should.Throw<LocalStackRefusal>(() =>
                LocalDatabase.ValidateConnection(connection, "fictional")
            );
        var id = new string('a', 32);
        LocalDatabase.ValidateOwner([id], id);
        Should.Throw<LocalStackRefusal>(() => LocalDatabase.ValidateOwner([], id));
        Should.Throw<LocalStackRefusal>(() => LocalDatabase.ValidateOwner([id, id], id));
        Should.Throw<LocalStackRefusal>(() =>
            LocalDatabase.ValidateOwner([new string('b', 32)], id)
        );
    }

    [Fact]
    public void Provisioning_uses_fresh_identity_encrypted_rings_and_both_clock_instants_without_overwrite()
    {
        using var fixture = new OwnedDirectory();
        var state = LocalStackState.ReadLocal(fixture.StateDir, fixture.Root);
        // Simulate execution years after the immutable E2E instant.
        var wall = new DateTimeOffset(2032, 3, 1, 12, 0, 0, TimeSpan.Zero);
        LocalProvisioning.Provision(state, fixture.Root, wall);
        var privateRoot = Path.Combine(fixture.StateDir, "private");
        var secrets = JsonNode.Parse(
            File.ReadAllText(Path.Combine(privateRoot, "provision.json"))
        )!;
        var password = secrets["certificatePassword"]!.GetValue<string>();
        password.Length.ShouldBe(64);
        var credentials = JsonNode
            .Parse(File.ReadAllText(Path.Combine(privateRoot, "credentials.json")))!
            .AsArray();
        credentials.Count.ShouldBe(2);
        credentials[0]!["subject"]!
            .GetValue<Guid>()
            .ShouldNotBe(credentials[1]!["subject"]!.GetValue<Guid>());
        credentials[0]!["password"]!
            .GetValue<string>()
            .ShouldNotBe(credentials[1]!["password"]!.GetValue<string>());
        var realm = JsonNode.Parse(
            File.ReadAllText(Path.Combine(privateRoot, "realm", "travel-realm.json"))
        )!;
        realm["users"]!.AsArray().Count.ShouldBe(2);
        realm["clients"]!.AsArray().Count.ShouldBe(1);
        var client = realm["clients"]![0]!;
        client["publicClient"]!.GetValue<bool>().ShouldBeTrue();
        client["directAccessGrantsEnabled"]!.GetValue<bool>().ShouldBeFalse();
        client["attributes"]!["pkce.code.challenge.method"]!.GetValue<string>().ShouldBe("S256");
        client["webOrigins"]![0]!.GetValue<string>().ShouldBe("http://127.0.0.1:4206");
        client["redirectUris"]![0]!.GetValue<string>().ShouldBe("http://127.0.0.1:4206/*");
        var keyRoot = Path.Combine(privateRoot, "keys", "trips");
        var options = Options.Create(
            new TripsProtectionOptions
            {
                KeyRingPath = Path.Combine(keyRoot, "ring"),
                ActiveCertificatePath = Path.Combine(keyRoot, "certificate.pfx"),
                ActiveCertificatePassword = password,
            }
        );
        using var fixedProvider = new TripsProtectionProvider(options, new FixedE2eTimeProvider());
        using var realProvider = new TripsProtectionProvider(
            options,
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(wall)
        );
        fixedProvider.IsReady.ShouldBeTrue();
        realProvider.IsReady.ShouldBeTrue();
        foreach (var module in new[] { "trips", "flights", "support" })
        {
            var xml = File.ReadAllText(
                Directory
                    .GetFiles(Path.Combine(privateRoot, "keys", module, "ring"), "*.xml")
                    .Single()
            );
            xml.ShouldContain("encryptedSecret");
            xml.ShouldNotContain("<masterKey");
        }
        var original = File.ReadAllBytes(Path.Combine(privateRoot, "credentials.json"));
        Should.Throw<LocalStackRefusal>(() =>
            LocalProvisioning.Provision(state, fixture.Root, wall)
        );
        File.ReadAllBytes(Path.Combine(privateRoot, "credentials.json")).ShouldBe(original);
    }

    [Fact]
    public void Snapshot_contains_only_metadata_and_hashes_never_protected_payload()
    {
        var entity = new TripPlanEntity
        {
            TripId = Guid.NewGuid(),
            OwnerIssuer = "http://127.0.0.1:8186/realms/travel",
            OwnerSubject = Guid.NewGuid(),
            Revision = 2,
            CreatedAt = FixedE2eTimeProvider.Instant,
            UpdatedAt = FixedE2eTimeProvider.Instant,
            ProtectedPayload = "fictional-private-ciphertext",
        };
        var snapshot = LocalDatabase.Snapshot(
            entity,
            new Dictionary<string, string> { ["trips/ring/key.xml"] = new string('a', 64) }
        );
        snapshot.PayloadLength.ShouldBe(Encoding.UTF8.GetByteCount(entity.ProtectedPayload));
        snapshot.PayloadSha256.ShouldBe(
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(entity.ProtectedPayload))
            )
        );
        var json = JsonSerializer.Serialize(snapshot, LocalStackJson.Options);
        json.ShouldNotContain("fictional-private-ciphertext");
        json.ShouldNotContain("ProtectedPayload");
        json.ShouldContain("payloadSha256");
    }

    [Fact]
    public void Readiness_requires_Trips_and_local_dependencies_and_never_exports_descriptions()
    {
        const string healthy = """
            {"status":"Degraded","checks":{"trips":{"status":"Healthy","description":"private secret"},"postgres":{"status":"Healthy"},"redis":{"status":"Healthy"},"nats":{"status":"Healthy"},"duffel":{"status":"Degraded"}}}
            """;
        var facts = LocalHealth.ParseDependencies(healthy);
        facts["trips"].ShouldBe("Healthy");
        facts["duffel"].ShouldBe("Degraded");
        LocalHealth.RequiredHealthy(facts).ShouldBeTrue();
        LocalHealth
            .RequiredHealthy(
                LocalHealth.ParseDependencies(
                    healthy.Replace(
                        "\"trips\":{\"status\":\"Healthy\"",
                        "\"trips\":{\"status\":\"Degraded\"",
                        StringComparison.Ordinal
                    )
                )
            )
            .ShouldBeFalse();
        LocalHealth
            .RequiredHealthy(new Dictionary<string, string> { ["trips"] = "Healthy" })
            .ShouldBeFalse();
        JsonSerializer.Serialize(facts).ShouldNotContain("private secret");
        Should.Throw<LocalStackRefusal>(() =>
            LocalHealth.ParseDependencies(new string('x', 65537))
        );
        E2eHostFactory.ValidateListeners(["http://0.0.0.0:5099", "http://127.0.0.1:5098"]);
        Should.Throw<LocalStackRefusal>(() =>
            E2eHostFactory.ValidateListeners(["http://127.0.0.1:12345"])
        );
    }

    [Theory]
    [InlineData("moscow")]
    [InlineData("saint-petersburg")]
    public void Application_clock_is_fixed_but_JWT_clock_and_validation_stay_real(string profile)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication().AddJwtBearer();
        E2eHostFactory.ConfigureClocks(services, profile);
        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow()
            .ShouldBe(DateTimeOffset.Parse(FixedE2eTimeProvider.ProfileInstant(profile)));
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        jwt.TimeProvider.ShouldBeSameAs(TimeProvider.System);
        jwt.TokenValidationParameters.ValidateLifetime.ShouldBeTrue();
        jwt.TokenValidationParameters.ValidateIssuer.ShouldBeTrue();
        jwt.TokenValidationParameters.ValidateAudience.ShouldBeTrue();
        jwt.TokenValidationParameters.RequireSignedTokens.ShouldBeTrue();
        Should.Throw<LocalStackRefusal>(() => E2eHostFactory.ValidateMode("demo", "Development"));
        Should.Throw<LocalStackRefusal>(() => E2eHostFactory.ValidateMode("e2e", "Production"));
    }

    private sealed class OwnedDirectory : IDisposable
    {
        internal string Root { get; } =
            Path.Combine(Path.GetTempPath(), "travel-trips-tool-" + Guid.NewGuid().ToString("N"));
        internal string StateDir { get; }
        internal string StateFile => Path.Combine(StateDir, "state.json");

        internal OwnedDirectory()
        {
            var id = Guid.NewGuid().ToString("N");
            StateDir = Path.Combine(Root, "TestResults", "trips-local-stack", "e2e-" + id);
            Directory.CreateDirectory(StateDir);
            Directory.CreateDirectory(Path.Combine(Root, "infra", "keycloak"));
            File.Copy(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "SupportOperatorIssuanceRealm.json"
                ),
                Path.Combine(Root, "infra", "keycloak", "travel-realm.json")
            );
            File.WriteAllText(
                StateFile,
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        kind = "travel-trips-local-stack",
                        id,
                        mode = "e2e",
                        repoRoot = Root,
                        projectName = "travel-trips-e2e-" + id,
                        webPort = 4206,
                        oidcPort = 8186,
                        applicationAsOfUtc = "2026-10-24T12:00:00Z",
                        createdAtUtc = DateTimeOffset.UtcNow,
                        dockerEndpoint = "unix:///var/run/docker.sock",
                        dockerId = "fictional-local-daemon",
                    }
                )
            );
        }

        public void Dispose()
        {
            var path = Path.GetFullPath(Root);
            if (
                !path.StartsWith(
                    Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase
                )
                || !Path.GetFileName(path)
                    .StartsWith("travel-trips-tool-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("Unexpected fixture directory.");
            Directory.Delete(path, true);
        }
    }
}
