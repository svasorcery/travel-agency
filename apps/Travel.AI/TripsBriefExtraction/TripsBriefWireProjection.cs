using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Travel.AI.TripsBriefExtraction;

// Provider dialect only. Local parsing and candidate validation retain every original bound.
public static class TripsBriefWireProjection
{
    public const string Version = "trips-anthropic-wire-v1";
    private static readonly JsonElement Projected = Project();

    public static JsonElement Schema() => Projected.Clone();

    public static string Sha256() =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Projected)));

    private static JsonElement Project()
    {
        var node = JsonNode.Parse(TripsBriefExtractor.LocalSchema().GetRawText())!;
        Visit(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void Visit(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("maxItems");
            foreach (var value in obj.ToArray())
                Visit(value.Value);
        }
        else if (node is JsonArray array)
            foreach (var value in array)
                Visit(value);
    }
}
