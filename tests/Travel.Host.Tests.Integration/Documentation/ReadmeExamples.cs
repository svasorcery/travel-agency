using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Travel.Modules.Flights.Api.Contracts;

namespace Travel.Host.Tests.Integration.Documentation;

internal sealed record ReadmeExample(
    string Id,
    string Method,
    string Path,
    Dictionary<string, string> PathParameters,
    Dictionary<string, string> Headers,
    JsonElement Body
);

internal static class ReadmeExamples
{
    public static readonly Guid AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly JsonSerializerOptions StrictWebJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    private static readonly Lazy<IReadOnlyList<ReadmeExample>> ExampleLoader = new(() =>
    {
        var root = FindRoot();
        var path = System.IO.Path.Combine(root, "docs", "examples", "flights-requests.json");
        var examples = JsonSerializer.Deserialize<ReadmeExample[]>(
            File.ReadAllText(path),
            StrictWebJson
        );
        if (examples is null || examples.Length != 6)
            throw new InvalidOperationException("README catalog must contain six examples.");
        return examples;
    });

    public static IReadOnlyList<ReadmeExample> Catalog => ExampleLoader.Value;

    public static ReadmeExample Get(string id) => Catalog.Single(example => example.Id == id);

    public static string ResolvedBody(string id) => Resolve(Get(id).Body.GetRawText());

    public static object ValidateBody(string id, string json) =>
        id switch
        {
            "search" => Validate<SearchRequest>(json),
            "nlSearch" => Validate<NlSearchRequest>(json),
            "quote" => Validate<QuoteOfferRequest>(json),
            "hold" => Validate<HoldOfferRequest>(json),
            "confirm" => Validate<ConfirmOrderRequest>(json),
            _ => throw new ArgumentException("No JSON body for example " + id, nameof(id)),
        };

    public static HttpRequestMessage CreateRequest(string id)
    {
        var example = Get(id);
        var route = example.Path;
        foreach (var (name, value) in example.PathParameters)
            route = route.Replace("{" + name + "}", Resolve(value), StringComparison.Ordinal);

        var request = new HttpRequestMessage(new HttpMethod(example.Method), route);
        foreach (var (name, value) in example.Headers)
        {
            if (name == "Content-Type")
                continue;
            request.Headers.TryAddWithoutValidation(name, Resolve(value));
        }

        if (example.Body.ValueKind is not JsonValueKind.Null)
        {
            var json = ResolvedBody(id);
            ValidateBody(id, json);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static T Validate<T>(string json)
        where T : class =>
        JsonSerializer.Deserialize<T>(json, StrictWebJson)
        ?? throw new JsonException(typeof(T).Name + " body deserialized to null.");

    private static string Resolve(string value) =>
        value
            .Replace("{{departureDate}}", "2026-10-23", StringComparison.Ordinal)
            .Replace("{{providerOfferRef}}", "offer_demo", StringComparison.Ordinal)
            .Replace("{{aggregateId}}", AggregateId.ToString(), StringComparison.Ordinal)
            .Replace("{{jwt}}", "fixture-jwt", StringComparison.Ordinal)
            .Replace(
                "{{holdIdempotencyKey}}",
                "33333333-3333-3333-3333-333333333333",
                StringComparison.Ordinal
            )
            .Replace(
                "{{confirmIdempotencyKey}}",
                "44444444-4444-4444-4444-444444444444",
                StringComparison.Ordinal
            );

    private static string FindRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Travel.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Travel.slnx not found.");
    }
}
