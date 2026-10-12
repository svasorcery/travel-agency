using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

// Initialized only by SupportOperatorIssuanceTests. It never starts AppHost/Host or uses
// an existing realm, users, credentials, database, keys, provider or model.
public sealed partial class SupportOperatorIssuanceFixture : IAsyncLifetime
{
    public const string OperatorClient = "travel-support-operator";
    public const string CustomerClient = "travel-web";
    public const string OperatorScope = "support:operator";
    public const string OperatorRole = "support-operator";
    private const string Realm = "travel";

    // Same tag as the already pinned Aspire.Hosting.Keycloak integration.
    private const string Image = "quay.io/keycloak/keycloak:26.6";
    private IContainer? container;
    private HttpClient? admin;
    private JsonWebKeySet signingKeys = default!;
    private Uri baseAddress = default!;

    public string Issuer => new Uri(baseAddress, $"realms/{Realm}").AbsoluteUri;
    public TestIdentity Operator { get; private set; } = default!;
    public TestIdentity Customer { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        var adminName = $"ci-{Guid.NewGuid():N}";
        var adminPassword = RandomSecret();
        try
        {
            container = new ContainerBuilder(Image)
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", adminName)
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", adminPassword)
                .WithPortBinding(8080, true)
                .WithResourceMapping(
                    CloneSourceRealm(),
                    "/opt/keycloak/data/import/travel-realm.json"
                )
                .WithCommand("start-dev", "--import-realm", "--log-level=warn")
                .WithWaitStrategy(
                    Wait.ForUnixContainer()
                        .UntilHttpRequestIsSucceeded(
                            request =>
                                request
                                    .ForPort(8080)
                                    .ForPath($"/realms/{Realm}/.well-known/openid-configuration")
                                    .ForStatusCode(HttpStatusCode.OK),
                            wait => wait.WithTimeout(TimeSpan.FromMinutes(2))
                        )
                )
                .Build();
            await container.StartAsync(ct);
            baseAddress = new Uri(
                $"http://{container.Hostname}:{container.GetMappedPublicPort(8080)}/"
            );
            admin = new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(20),
            };
            // The bootstrap admin's password grant provisions this isolated CI realm only.
            // Every application-token proof below uses authorization code + PKCE.
            using var bootstrapForm = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = "admin-cli",
                    ["grant_type"] = "password",
                    ["username"] = adminName,
                    ["password"] = adminPassword,
                }
            );
            using var bootstrapResponse = await admin.PostAsync(
                "realms/master/protocol/openid-connect/token",
                bootstrapForm,
                ct
            );
            RequireStatus(bootstrapResponse, HttpStatusCode.OK, "CI admin authentication");
            using var bootstrapJson = await ReadJsonAsync(bootstrapResponse, ct);
            admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                RequiredString(bootstrapJson.RootElement, "access_token")
            );
            Operator = await CreateIdentityAsync(true, ct);
            Customer = await CreateIdentityAsync(false, ct);
            using var keysResponse = await admin.GetAsync(
                $"realms/{Realm}/protocol/openid-connect/certs",
                ct
            );
            RequireStatus(keysResponse, HttpStatusCode.OK, "CI issuer signing keys");
            signingKeys = new JsonWebKeySet(await keysResponse.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            await DisposeAsync();
            // Do not attach container logs, HTTP bodies, passwords, codes or tokens to failures.
            throw new InvalidOperationException(
                "Disposable CI Keycloak issuance fixture initialization failed."
            );
        }
    }

    private async Task<TestIdentity> CreateIdentityAsync(bool isOperator, CancellationToken ct)
    {
        var username = $"ci-{Guid.NewGuid():N}";
        var password = RandomSecret();
        using var userBody = JsonContent.Create(
            new
            {
                username,
                enabled = true,
                email = $"{username}@example.invalid",
                emailVerified = true,
                firstName = "Fictional",
                lastName = "CI",
                requiredActions = Array.Empty<string>(),
                credentials = new[]
                {
                    new
                    {
                        type = "password",
                        value = password,
                        temporary = false,
                    },
                },
            }
        );
        using var created = await admin!.PostAsync($"admin/realms/{Realm}/users", userBody, ct);
        RequireStatus(created, HttpStatusCode.Created, "CI identity creation");
        // Keycloak creates the identity; retain its actual GUID for all token/client checks.
        var location = created.Headers.Location;
        if (
            location is null
            || !location.IsAbsoluteUri
            || !Guid.TryParse(location.Segments[^1], out var subject)
            || subject == Guid.Empty
        )
            throw new InvalidOperationException(
                "CI identity creation did not return a GUID subject."
            );
        var identity = new TestIdentity(subject, username, password);
        if (isOperator)
        {
            using var roleResponse = await admin.GetAsync(
                $"admin/realms/{Realm}/roles/{OperatorRole}",
                ct
            );
            RequireStatus(roleResponse, HttpStatusCode.OK, "CI operator role lookup");
            using var role = await ReadJsonAsync(roleResponse, ct);
            using var mapping = JsonContent.Create(new[] { role.RootElement });
            using var granted = await admin.PostAsync(
                $"admin/realms/{Realm}/users/{identity.Subject}/role-mappings/realm",
                mapping,
                ct
            );
            RequireStatus(granted, HttpStatusCode.NoContent, "CI-only operator entitlement");
        }
        using var effectiveRoles = await admin.GetAsync(
            $"admin/realms/{Realm}/users/{identity.Subject}/role-mappings/realm/composite",
            ct
        );
        RequireStatus(effectiveRoles, HttpStatusCode.OK, "CI identity entitlement check");
        using var roles = await ReadJsonAsync(effectiveRoles, ct);
        if (
            roles
                .RootElement.EnumerateArray()
                .Any(role => RequiredString(role, "name") == OperatorRole) != isOperator
        )
            throw new InvalidOperationException(
                "CI operator role unexpectedly inherited or missing."
            );
        return identity;
    }

    public async ValueTask DisposeAsync()
    {
        admin?.Dispose();
        if (container is not null)
            await container.DisposeAsync();
    }

    private static string RandomSecret() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));

    private static void RequireStatus(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string phase
    )
    {
        if (response.StatusCode != expected)
            throw new InvalidOperationException(
                $"{phase} returned unexpected HTTP {(int)response.StatusCode}."
            );
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string RequiredString(JsonElement json, string property)
    {
        if (
            !json.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString())
        )
            throw new InvalidOperationException("CI issuer response is missing a required field.");
        return value.GetString()!;
    }

    // A class instead of a record: xUnit/Shouldly failure formatting must not print credentials.
    public sealed class TestIdentity(Guid subject, string username, string password)
    {
        public Guid Subject { get; } = subject;
        internal string Username { get; } = username;
        internal string Password { get; } = password;
    }
}
