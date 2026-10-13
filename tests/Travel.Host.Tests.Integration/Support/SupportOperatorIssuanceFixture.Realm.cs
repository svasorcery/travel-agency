using System.Text;
using System.Text.Json.Nodes;

namespace Travel.Host.Tests.Integration.Support;

public sealed partial class SupportOperatorIssuanceFixture
{
    private static byte[] CloneSourceRealm()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "SupportOperatorIssuanceRealm.json"
        );
        var source =
            JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException(
                "Support issuance realm source fixture is missing."
            );
        // An allowlist deliberately excludes source users, client secrets, credentials and keys.
        // Roles/mappers/scopes are imported unchanged: the test cannot manufacture the permission
        // mapping it claims to prove. Only identities are provisioned by the private CI admin.
        var clone = CopyFields(
            source,
            "realm",
            "enabled",
            "registrationAllowed",
            "loginWithEmailAllowed",
            "duplicateEmailsAllowed",
            "resetPasswordAllowed",
            "roles",
            "defaultRole",
            "defaultRoles",
            "defaultDefaultClientScopes",
            "defaultOptionalClientScopes",
            "clientScopes",
            "scopeMappings",
            "clientScopeMappings"
        );
        var clients = new JsonArray();
        foreach (var client in source["clients"]!.AsArray().OfType<JsonObject>())
        {
            if (client["clientId"]?.GetValue<string>() is not (CustomerClient or OperatorClient))
                continue;
            clients.Add(
                CopyFields(
                    client,
                    "clientId",
                    "protocol",
                    "enabled",
                    "publicClient",
                    "standardFlowEnabled",
                    "implicitFlowEnabled",
                    "directAccessGrantsEnabled",
                    "serviceAccountsEnabled",
                    "fullScopeAllowed",
                    "redirectUris",
                    "webOrigins",
                    "attributes",
                    "defaultClientScopes",
                    "optionalClientScopes",
                    "protocolMappers"
                )
            );
        }
        if (clients.Count != 2)
            throw new InvalidOperationException(
                "Both source application clients are required for issuance proof."
            );
        clone["clients"] = clients;
        return Encoding.UTF8.GetBytes(clone.ToJsonString());
    }

    private static JsonObject CopyFields(JsonObject source, params string[] fields)
    {
        var result = new JsonObject();
        foreach (var name in fields)
            if (source.TryGetPropertyValue(name, out var value))
                result[name] = value?.DeepClone();
        return result;
    }
}
