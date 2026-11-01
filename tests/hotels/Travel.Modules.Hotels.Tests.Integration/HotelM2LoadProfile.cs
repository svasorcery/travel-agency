using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

// Finite fictional data only: selecting this object never enables a module/transport.
public sealed class HotelM2LoadProfile
{
    public HotelM2LoadProfile(string code, DateOnly checkIn)
    {
        if (code is not ("S" or "L"))
            throw new ArgumentOutOfRangeException(nameof(code));
        Code = code;
        CheckIn = checkIn;
    }

    public string Code { get; }
    public DateOnly CheckIn { get; }
    public DateOnly CheckOut => CheckIn.AddDays(2);
    public int Adults => Code == "S" ? 1 : 2;
    public int PropertiesPerSource => Code == "S" ? 9 : 100;
    public int MatchedPairs => Code == "S" ? 8 : 100;
    public int Groups => Code == "S" ? 10 : 100;
    public int Leaves => PropertiesPerSource * 2;
    public int OpaqueKeyBytes => Code == "S" ? 64 : 2048;
    public int[] HotelbedsIds => Enumerable.Range(900001, PropertiesPerSource).ToArray();
    public string Revision => "load-" + Code.ToLowerInvariant() + "-20261029-v1-" + Date(CheckIn);
    public DateTimeOffset FixtureEpoch =>
        new(CheckIn.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    // At most seven windows in360s, plus two setup availability payloads. No state reset.
    public int MaximumDuffelRateEntries => 2 * PropertiesPerSource * (2 + 7 * 24);
    public int MaximumHotelbedsRateEntries => PropertiesPerSource * (2 + 7 * 24) + 128;
    public int MaximumQuoteEntries => 64;
    public int MaximumBookingEntries => 64;

    public string Currency(string source, int index) =>
        source == "hotelbeds" && index <= MatchedPairs && index % 5 == 0 ? "EUR" : "GBP";

    public bool UnknownLocal(string source, int index) =>
        source == "hotelbeds" && index <= MatchedPairs && index % 7 == 0;

    public string Amount(string source) => source == "hotelbeds" ? "130.25" : "120.25";

    public HotelProperty Property(string source, int index)
    {
        if (index < 1 || index > PropertiesPerSource)
            throw new ArgumentOutOfRangeException(nameof(index));
        var id =
            source == "hotelbeds"
                ? (900000 + index).ToString(CultureInfo.InvariantCulture)
                : $"acc_load_{index:000}";
        var address =
            index == 1 ? "1 Owned Fictional Square"
            : source == "hotelbeds" ? null
            : $"Owned load square {index:000}";
        return new(
            id,
            $"Owned Load Hotel {index:000}",
            address,
            source == "hotelbeds" ? null : "3",
            source == "hotelbeds" ? null : "Duffel accommodation rating"
        );
    }

    public HotelReviewedMap Map(HotelSourceIdentity duffel, HotelSourceIdentity hotelbeds) =>
        new(
            Enumerable
                .Range(1, MatchedPairs)
                .Select(index =>
                {
                    var left = Property("duffel-stays", index);
                    var right = Property("hotelbeds", index);
                    return new ReviewedHotelPair(
                        $"load-{Code}-{index:000}",
                        Revision,
                        new(duffel, left.Id),
                        new(hotelbeds, right.Id),
                        "Project-owned fictional building record " + index,
                        "fictional-load-profile-owner",
                        "Same fictional building in the declared catalog",
                        FixtureEpoch.AddDays(-1),
                        FixtureEpoch.AddDays(14),
                        "Fictional",
                        HotelPropertyMatching.PropertyFingerprint(left),
                        HotelPropertyMatching.PropertyFingerprint(right)
                    );
                })
                .ToArray()
        );

    public string HotelbedsKey(int ordinal, int propertyId, bool prepared = false)
    {
        var prefix = $"hbx_load_{Code}_{ordinal}_{propertyId}_{(prepared ? "p" : "a")}_";
        if (prefix.Length > OpaqueKeyBytes)
            throw new InvalidOperationException("Opaque fixture key envelope exceeded.");
        return prefix + new string('x', OpaqueKeyBytes - prefix.Length);
    }

    public Guid Id(string role, int index) =>
        new(
            SHA256.HashData(
                Encoding.UTF8.GetBytes($"20261029:{Code}:{Date(CheckIn)}:{role}:{index}")
            )[..16]
        );

    public string PurchaseSource(int index) =>
        (index % 2 == 0) == (Code == "S") ? "hotelbeds" : "duffel-stays";

    public static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
