using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Infrastructure;

public static class YandexStationCatalogueParser
{
    // Incremental UTF-8 tokenizer: the only raw retained buffer is32KiB. Country
    // candidates refer to bounded parent metadata, so JSON property order is irrelevant.
    public static async Task<ErrorOr<CatalogueData>> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[32768];
        var state = new JsonReaderState(new JsonReaderOptions { MaxDepth = 32 });
        var builder = new CatalogueBuilder();
        var retained = 0;
        long bytes = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(retained), cancellationToken);
                bytes += read;
                if (bytes > 64L * 1024 * 1024)
                    throw new JsonException();
                var available = retained + read;
                var consumed = Process(buffer.AsSpan(0, available), read == 0, ref state, builder);
                retained = available - consumed;
                if (retained > 16384 || (read == 0 && retained != 0))
                    throw new JsonException();
                buffer.AsSpan(consumed, retained).CopyTo(buffer);
                if (read == 0)
                    break;
            }
            return builder.Finish();
        }
        catch (JsonException)
        {
            return Error.Failure("catalogue-invalid");
        }
    }

    private static int Process(
        ReadOnlySpan<byte> bytes,
        bool final,
        ref JsonReaderState state,
        CatalogueBuilder builder
    )
    {
        var reader = new Utf8JsonReader(bytes, final, state);
        while (reader.Read())
        {
            if (reader.ValueSpan.Length > 16384)
                throw new JsonException();
            builder.Accept(ref reader);
        }
        state = reader.CurrentState;
        return checked((int)reader.BytesConsumed);
    }

    private sealed class Node(string kind, Node? parent, bool array)
    {
        public string Kind { get; } = kind;
        public Node? Parent { get; } = parent;
        public bool Array { get; } = array;
        public string? Property { get; set; }
        public string? Title { get; set; }
        public string? Code { get; set; }
        public string? Transport { get; set; }
        public string? Type { get; set; }
        public long Charge { get; set; }
        public bool Limited { get; set; }
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CatalogueBuilder
    {
        private readonly Stack<Node> stack = new();
        private readonly List<Node?> candidates = [];
        private readonly Dictionary<string, RailStation> stations = new(StringComparer.Ordinal);
        private bool countries;
        private bool rf;
        private bool limited;
        private long stagedBytes;
        private long payload;

        public void Accept(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var node = stack.Peek();
                var name = reader.GetString()!;
                node.Property = name;
                if (
                    node.Kind != "unknown"
                    && name
                        is "countries"
                            or "regions"
                            or "settlements"
                            or "stations"
                            or "codes"
                            or "yandex_code"
                            or "title"
                            or "transport_type"
                            or "station_type"
                )
                {
                    if (!node.Seen.Add(name))
                        throw new JsonException();
                }
                return;
            }
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                var parent = stack.TryPeek(out var top) ? top : null;
                var name = parent?.Property ?? "root";
                var array = reader.TokenType == JsonTokenType.StartArray;
                if (parent is null && array)
                    throw new JsonException();
                var kind =
                    parent is null ? "root"
                    : parent.Array
                        ? parent.Kind switch
                        {
                            "countries" => "country",
                            "regions" => "region",
                            "settlements" => "settlement",
                            "stations" => "station",
                            _ => "unknown",
                        }
                    : (parent.Kind, name) switch
                    {
                        ("root", "countries") => "countries",
                        ("country", "regions") => "regions",
                        ("region", "settlements") => "settlements",
                        ("settlement", "stations") => "stations",
                        ("country" or "region" or "settlement" or "station", "codes") => "codes",
                        _ => "unknown",
                    };
                if (kind == "countries")
                {
                    if (!array || parent?.Kind != "root")
                        throw new JsonException();
                    countries = true;
                }
                if (kind is "regions" or "settlements" or "stations" && !array)
                    throw new JsonException();
                if (kind is "country" or "region" or "settlement" or "station" && array)
                    throw new JsonException();
                stack.Push(new Node(kind, parent, array));
                return;
            }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                var node = stack.Pop();
                if (node.Kind == "station")
                {
                    if (node.Transport is "train" or "suburban" or "Поезд" or "Электричка")
                        candidates.Add(node);
                    else
                    {
                        if (
                            node.Transport
                            is not (
                                "plane"
                                or "bus"
                                or "water"
                                or "helicopter"
                                or "sea"
                                or "river"
                                or "Автобус"
                                or "Самолёт"
                            )
                        )
                        {
                            var country = node.Parent?.Parent?.Parent?.Parent?.Parent?.Parent;
                            if (country is not null)
                                country.Limited = true;
                        }
                        stagedBytes -= node.Charge;
                    }
                    if (candidates.Count > 100000 || payload + stagedBytes > 48L * 1024 * 1024)
                        throw new JsonException();
                }
                if (node.Kind == "country")
                    PublishCountry(node);
                return;
            }
            if (stack.Count == 0)
                throw new JsonException();
            var current = stack.Peek();
            var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (current.Kind == "unknown")
                return;
            var charge = current.Kind == "codes" ? current.Parent! : current;
            if (
                value is not null
                && (
                    current.Property
                    is "title"
                        or "transport_type"
                        or "station_type"
                        or "yandex_code"
                )
            )
            {
                if (!RailRules.LabelValid(value))
                    throw new JsonException();
                charge.Charge += Size(value);
                stagedBytes += Size(value);
                if (payload + stagedBytes > 48L * 1024 * 1024)
                    throw new JsonException();
            }
            switch (current.Property)
            {
                case "countries" or "regions" or "settlements" or "stations" or "codes":
                    throw new JsonException();
                case "title":
                    current.Title = value;
                    break;
                case "transport_type":
                    current.Transport = value;
                    break;
                case "station_type":
                    current.Type = value;
                    break;
                case "yandex_code" when current.Kind == "codes":
                    current.Parent!.Code = value;
                    break;
            }
        }

        private void PublishCountry(Node country)
        {
            if (country.Code == "l225")
            {
                if (rf)
                    throw new JsonException();
                rf = true;
                limited |= country.Limited;
                for (var index = 0; index < candidates.Count; index++)
                {
                    var node = candidates[index]!;
                    candidates[index] = null;
                    stagedBytes -= node.Charge;
                    var rail = node.Transport is "train" or "suburban" or "Поезд" or "Электричка";
                    var knownOther =
                        node.Transport
                        is "plane"
                            or "bus"
                            or "water"
                            or "helicopter"
                            or "sea"
                            or "river"
                            or "Автобус"
                            or "Самолёт";
                    if (!rail)
                    {
                        if (!knownOther)
                            limited = true;
                        continue;
                    }
                    if (
                        node.Type
                        is not (
                            "station"
                            or "train_station"
                            or "platform"
                            or "stop"
                            or "станция"
                            or "вокзал"
                            or "платформа"
                            or "остановочный пункт"
                        )
                    )
                    {
                        limited = true;
                        continue;
                    }
                    var reference = StationRef.Create("yandex-rasp", node.Code ?? "");
                    if (
                        reference.IsError
                        || node.Code is null
                        || node.Code.Length < 2
                        || node.Code[0] != 's'
                        || node.Code.AsSpan(1).IndexOfAnyExceptInRange('0', '9') >= 0
                        || string.IsNullOrWhiteSpace(node.Title)
                        || !RailRules.LabelValid(node.Title)
                    )
                        throw new JsonException();
                    var settlement = node.Parent?.Parent;
                    var region = settlement?.Parent?.Parent;
                    if (
                        (settlement?.Title is { } s && !RailRules.LabelValid(s))
                        || (region?.Title is { } r && !RailRules.LabelValid(r))
                    )
                        throw new JsonException();
                    var station = new RailStation(
                        reference.Value,
                        node.Title,
                        settlement?.Title,
                        region?.Title
                    );
                    if (stations.TryGetValue(reference.Value.Code, out var existing))
                    {
                        if (existing != station)
                            throw new JsonException();
                    }
                    else
                    {
                        stations.Add(reference.Value.Code, station);
                        payload +=
                            Size(station.Name)
                            + Size(station.Settlement)
                            + Size(station.Region)
                            + Size(station.Reference.Code)
                            + 64;
                        if (stations.Count > 100000 || payload + stagedBytes > 48L * 1024 * 1024)
                            throw new JsonException();
                    }
                }
            }
            candidates.Clear();
            stagedBytes = 0;
        }

        public ErrorOr<CatalogueData> Finish() =>
            countries && rf && stations.Count > 0 && stack.Count == 0
                ? new CatalogueData(stations.Values.ToImmutableArray(), limited, payload)
                : Error.Failure("catalogue-unavailable");

        private static int Size(string? text) =>
            text is null ? 0 : Encoding.UTF8.GetByteCount(text);
    }
}
