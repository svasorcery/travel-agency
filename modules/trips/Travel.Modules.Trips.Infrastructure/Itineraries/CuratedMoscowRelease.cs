namespace Travel.Modules.Trips.Infrastructure.Itineraries;

// Trusted caller-owned release identity. Never infer this descriptor from the runtime directory.
public sealed class CuratedMoscowRelease
{
    public const string ReviewedV1ManifestSha256 =
        "8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43";
    public static CuratedMoscowRelease ReviewedV1 { get; } =
        new("moscow-outdoor", "1", ReviewedV1ManifestSha256);
    public string DatasetId { get; }
    public string Version { get; }
    public string ManifestSha256 { get; }

    public CuratedMoscowRelease(string datasetId, string version, string manifestSha256)
    {
        if (datasetId != "moscow-outdoor")
            throw new ArgumentException(
                "Only the Moscow outdoor dataset is supported.",
                nameof(datasetId)
            );
        if (
            version is not { Length: >= 1 and <= 64 }
            || !version.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.')
        )
            throw new ArgumentException(
                "A canonical release version is required.",
                nameof(version)
            );
        if (
            manifestSha256 is not { Length: 64 }
            || !manifestSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
            || (version == "1" && manifestSha256 != ReviewedV1ManifestSha256)
        )
            throw new ArgumentException(
                "A trusted immutable manifest identity is required.",
                nameof(manifestSha256)
            );
        DatasetId = datasetId;
        Version = version;
        ManifestSha256 = manifestSha256;
    }
}
