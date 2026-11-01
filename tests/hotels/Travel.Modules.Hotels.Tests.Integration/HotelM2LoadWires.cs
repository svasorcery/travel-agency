using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed partial class HotelM2MemoryFixture
{
    public sealed partial class OwnedDuffelWire
    {
        private readonly ConcurrentDictionary<string, byte[]> loadRates = new(
            StringComparer.Ordinal
        );

        private JsonNode RateForResponse(string id) =>
            LoadProfile is null ? rates[id].DeepClone() : JsonNode.Parse(loadRates[id])!;

        private JsonNode RateForQuote(string id) =>
            LoadProfile is null ? rates[id] : JsonNode.Parse(loadRates[id])!;

        private HotelM2LoadProfile? loadProfile;
        public HotelM2LoadProfile? LoadProfile
        {
            get => loadProfile;
            set
            {
                lock (loadGate)
                {
                    if (
                        sequence != 0
                        || loadProfile is not null && !ReferenceEquals(loadProfile, value)
                    )
                        throw new InvalidOperationException(
                            "Load catalog is fixed at composition."
                        );
                    loadProfile = value;
                }
            }
        }
        public HotelM2LoadControl? LoadControl { get; set; }
        private readonly object loadGate = new();

        private void RejectLoad(string reason)
        {
            LoadControl?.Fail(reason);
            throw new InvalidOperationException("Owned load fixture invariant failed: " + reason);
        }

        private HttpResponseMessage LoadSearch(JsonNode body, int ordinal)
        {
            var p = LoadProfile!;
            if (
                body["rooms"]!.GetValue<int>() != 1
                || body["check_in_date"]!.GetValue<string>() != HotelM2LoadProfile.Date(p.CheckIn)
                || body["check_out_date"]!.GetValue<string>() != HotelM2LoadProfile.Date(p.CheckOut)
                || body["guests"]!.AsArray().Count != p.Adults
                || body["guests"]!.AsArray().Any(g => g!["type"]!.GetValue<string>() != "adult")
            )
                RejectLoad("FrozenDuffelScopeMismatch");
            lock (loadGate)
            {
                if (loadRates.Count + 2 * p.PropertiesPerSource > p.MaximumDuffelRateEntries)
                    RejectLoad("DuffelRetainedEnvelopeExceeded");
                var result = new JsonArray();
                for (var index = 1; index <= p.PropertiesPerSource; index++)
                {
                    var data = Template("search")["data"]!["results"]![0]!.DeepClone();
                    var property = p.Property("duffel-stays", index);
                    data["id"] = $"srr_load_{p.Code}_{ordinal}_{index}";
                    data["check_in_date"] = body["check_in_date"]!.DeepClone();
                    data["check_out_date"] = body["check_out_date"]!.DeepClone();
                    data["guests"] = body["guests"]!.DeepClone();
                    data["expires_at"] = Clock.GetUtcNow().AddMinutes(5);
                    data["cheapest_rate_total_amount"] = p.Amount("duffel-stays");
                    data["cheapest_rate_currency"] = p.Currency("duffel-stays", index);
                    var accommodation = data["accommodation"]!;
                    accommodation["id"] = property.Id;
                    accommodation["name"] = property.Name;
                    accommodation["location"] = new JsonObject
                    {
                        ["address"] = new JsonObject { ["line_one"] = property.Address },
                    };
                    var rate = accommodation["rooms"]![0]!["rates"]![0]!;
                    rate["id"] = $"rat_load_{p.Code}_{ordinal}_{index}";
                    rate["total_amount"] = p.Amount("duffel-stays");
                    rate["total_currency"] = p.Currency("duffel-stays", index);
                    rate["due_at_accommodation_amount"] = "0.00";
                    rate["due_at_accommodation_currency"] = p.Currency("duffel-stays", index);
                    rate["expires_at"] = Clock.GetUtcNow().AddMinutes(5);
                    var stored = JsonSerializer.SerializeToUtf8Bytes(data);
                    loadRates[data["id"]!.GetValue<string>()] = stored;
                    loadRates[rate["id"]!.GetValue<string>()] = stored;
                    result.Add(data);
                }
                return Reply(new JsonObject { ["data"] = new JsonObject { ["results"] = result } });
            }
        }

        private void ApplyLoadQuote(JsonNode data, JsonNode source)
        {
            if (LoadProfile is null)
                return;
            var rate = source["accommodation"]!["rooms"]![0]!["rates"]![0]!;
            foreach (
                var field in new[]
                {
                    "total_amount",
                    "total_currency",
                    "due_at_accommodation_amount",
                    "due_at_accommodation_currency",
                }
            )
                data[field] = rate[field]!.DeepClone();
        }
    }

    public sealed partial class OwnedHotelbedsWire
    {
        private string RateStorageKey(string key) =>
            LoadProfile is null
                ? key
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

        private HotelM2LoadProfile? loadProfile;
        public HotelM2LoadProfile? LoadProfile
        {
            get => loadProfile;
            set
            {
                lock (loadGate)
                {
                    if (
                        sequence != 0
                        || loadProfile is not null && !ReferenceEquals(loadProfile, value)
                    )
                        throw new InvalidOperationException(
                            "Load catalog is fixed at composition."
                        );
                    loadProfile = value;
                }
            }
        }
        public HotelM2LoadControl? LoadControl { get; set; }
        private readonly object loadGate = new();

        private void RejectLoad(string reason)
        {
            LoadControl?.Fail(reason);
            throw new InvalidOperationException("Owned load fixture invariant failed: " + reason);
        }

        private HttpResponseMessage LoadAvailability(JsonNode body, int ordinal)
        {
            var p = LoadProfile!;
            var ids = body["hotels"]?["hotel"]?.AsArray().Select(x => x!.GetValue<int>()).ToArray();
            if (
                ids is null
                || !ids.SequenceEqual(p.HotelbedsIds)
                || body["stay"]!["checkIn"]!.GetValue<string>()
                    != HotelM2LoadProfile.Date(p.CheckIn)
                || body["stay"]!["checkOut"]!.GetValue<string>()
                    != HotelM2LoadProfile.Date(p.CheckOut)
                || body["occupancies"]!.AsArray().Count != 1
                || body["occupancies"]![0]!["rooms"]!.GetValue<int>() != 1
                || body["occupancies"]![0]!["adults"]!.GetValue<int>() != p.Adults
                || body["occupancies"]![0]!["children"]!.GetValue<int>() != 0
            )
                RejectLoad("FrozenHotelbedsScopeMismatch");
            lock (loadGate)
            {
                if (rates.Count + ids!.Length > p.MaximumHotelbedsRateEntries)
                    RejectLoad("HotelbedsRetainedEnvelopeExceeded");
                var hotels = new JsonArray();
                foreach (var id in ids)
                {
                    var scope = new Scope(
                        HotelM2LoadProfile.Date(p.CheckIn),
                        HotelM2LoadProfile.Date(p.CheckOut),
                        p.Adults,
                        id
                    );
                    var key = p.HotelbedsKey(ordinal, id);
                    rates[RateStorageKey(key)] = scope;
                    hotels.Add(Hotel(scope, key, "RECHECK"));
                }
                return Reply(
                    new JsonObject
                    {
                        ["hotels"] = new JsonObject
                        {
                            ["checkIn"] = HotelM2LoadProfile.Date(p.CheckIn),
                            ["checkOut"] = HotelM2LoadProfile.Date(p.CheckOut),
                            ["hotels"] = hotels,
                        },
                    }
                );
            }
        }

        private JsonObject ApplyLoadHotel(JsonObject hotel, Scope scope)
        {
            if (LoadProfile is not { } p)
                return hotel;
            var index = scope.PropertyId - 900000;
            hotel["code"] = scope.PropertyId;
            hotel["name"] = p.Property("hotelbeds", index).Name;
            hotel["currency"] = p.Currency("hotelbeds", index);
            var rate = hotel["rooms"]![0]!["rates"]![0]!;
            rate["net"] = p.Amount("hotelbeds");
            rate["cancellationPolicies"]![0]!["amount"] = p.Amount("hotelbeds");
            if (p.UnknownLocal("hotelbeds", index))
                rate["taxes"]!["allIncluded"] = false;
            return hotel;
        }
    }
}
