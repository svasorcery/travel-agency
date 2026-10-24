using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Modules.Support.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Privacy;

namespace Travel.Trips.LocalStack;

public sealed record ProvisionSecrets(string DatabasePassword, string CertificatePassword);

public sealed record FictionalCredential(string Username, string Password, Guid Subject);

public static class LocalProvisioning
{
    public static void Provision(LocalStackState state, string repoRoot, DateTimeOffset wall)
    {
        // Revalidate immediately before writing, including after a caller retained a state object.
        state = LocalStackState.ReadLocal(state.StateDir, repoRoot);
        var privateRoot = Path.Combine(state.StateDir, "private");
        LocalStackState.RequireNoLinks(privateRoot);
        if (Directory.Exists(privateRoot) || File.Exists(privateRoot))
            throw new LocalStackRefusal(
                "Private state already exists; provisioning never overwrites it."
            );
        var realmPath = Path.Combine(repoRoot, "infra", "keycloak", "travel-realm.json");
        LocalStackState.RequireNoLinks(realmPath);
        var realm = JsonNode.Parse(File.ReadAllBytes(realmPath))!.AsObject();
        var canonical = realm["clients"]!
            .AsArray()
            .Single(c => c!["clientId"]!.GetValue<string>() == "travel-web")!;
        if (
            !canonical["publicClient"]!.GetValue<bool>()
            || !canonical["standardFlowEnabled"]!.GetValue<bool>()
            || canonical["directAccessGrantsEnabled"]!.GetValue<bool>()
            || canonical["attributes"]!["pkce.code.challenge.method"]!.GetValue<string>() != "S256"
        )
            throw new LocalStackRefusal("The canonical public PKCE client contract changed.");
        using var claim = new FileStream(
            Path.Combine(state.StateDir, ".provision.lock"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );
        CreatePrivateDirectory(privateRoot);
        var secrets = new ProvisionSecrets(NewPassword(), NewPassword());
        var credentials = new[]
        {
            new FictionalCredential("trips-a-" + state.Id[..8], NewPassword(), Guid.NewGuid()),
            new FictionalCredential("trips-b-" + state.Id[..8], NewPassword(), Guid.NewGuid()),
        };
        WritePrivateJson(Path.Combine(privateRoot, "provision.json"), secrets);
        WritePrivateJson(Path.Combine(privateRoot, "credentials.json"), credentials);
        var applicationInstant = state.Mode == "e2e" ? FixedE2eTimeProvider.Instant : wall;
        var activation = (wall < applicationInstant ? wall : applicationInstant).AddDays(-1);
        var expiration = (wall > applicationInstant ? wall : applicationInstant).AddDays(90);
        foreach (
            var module in new[]
            {
                (Name: "trips", Application: TripsProtectionProvider.ApplicationName),
                (Name: "flights", Application: FlightsPiiProtectionProvider.ApplicationName),
                (Name: "support", Application: SupportTextProtectionProvider.ApplicationName),
            }
        )
        {
            var directory = Path.Combine(privateRoot, "keys", module.Name);
            var ring = Path.Combine(directory, "ring");
            CreatePrivateDirectory(ring);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Fictional owned Trips " + module.Name,
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using var certificate = request.CreateSelfSigned(activation, expiration);
            WritePrivate(
                Path.Combine(directory, "certificate.pfx"),
                certificate.Export(X509ContentType.Pfx, secrets.CertificatePassword)
            );
            var services = new ServiceCollection();
            services.AddLogging();
            services
                .AddDataProtection()
                .SetApplicationName(module.Application)
                .PersistKeysToFileSystem(new DirectoryInfo(ring))
                .ProtectKeysWithCertificate(certificate);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IKeyManager>().CreateNewKey(activation, expiration);
            foreach (var key in Directory.GetFiles(ring, "*.xml"))
                RestrictFile(key);
        }
        var client = canonical.DeepClone();
        var origin = $"http://127.0.0.1:{state.WebPort}";
        client["redirectUris"] = new JsonArray(origin + "/*");
        client["webOrigins"] = new JsonArray(origin);
        client["implicitFlowEnabled"] = false;
        client["serviceAccountsEnabled"] = false;
        realm["clients"] = new JsonArray(client);
        realm["registrationAllowed"] = false;
        realm["resetPasswordAllowed"] = false;
        realm["users"] = new JsonArray(
            credentials
                .Select(user =>
                    (JsonNode)
                        new JsonObject
                        {
                            ["id"] = user.Subject.ToString(),
                            ["username"] = user.Username,
                            ["enabled"] = true,
                            ["email"] = user.Username + "@fictional.invalid",
                            ["emailVerified"] = true,
                            ["firstName"] = "Fictional",
                            ["lastName"] = "Traveler",
                            ["realmRoles"] = new JsonArray("user"),
                            ["credentials"] = new JsonArray(
                                new JsonObject
                                {
                                    ["type"] = "password",
                                    ["value"] = user.Password,
                                    ["temporary"] = false,
                                }
                            ),
                        }
                )
                .ToArray()
        );
        var realmDirectory = Path.Combine(privateRoot, "realm");
        CreatePrivateDirectory(realmDirectory);
        WritePrivateJson(Path.Combine(realmDirectory, "travel-realm.json"), realm);
    }

    public static ProvisionSecrets ReadSecrets(LocalStackState state)
    {
        var path = Path.Combine(state.StateDir, "private", "provision.json");
        LocalStackState.RequireNoLinks(path);
        if (new FileInfo(path).Length is <= 0 or > 4096)
            throw new LocalStackRefusal("Invalid private provisioning record.");
        var secrets = JsonSerializer.Deserialize<ProvisionSecrets>(
            File.ReadAllBytes(path),
            LocalStackJson.Options
        );
        if (
            secrets is null
            || !ValidPassword(secrets.DatabasePassword)
            || !ValidPassword(secrets.CertificatePassword)
        )
            throw new LocalStackRefusal("Invalid private provisioning record.");
        return secrets;
    }

    private static bool ValidPassword(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string NewPassword() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static void WritePrivateJson<T>(string path, T value) =>
        WritePrivate(path, JsonSerializer.SerializeToUtf8Bytes(value, LocalStackJson.Options));

    private static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }

    private static void WritePrivate(string path, byte[] bytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );
        RestrictFile(path);
        stream.Write(bytes);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
