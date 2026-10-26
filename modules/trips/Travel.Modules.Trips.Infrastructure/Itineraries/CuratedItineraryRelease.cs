namespace Travel.Modules.Trips.Infrastructure.Itineraries;

// Trusted caller-owned release identity. Never infer this descriptor from the runtime directory.
public sealed class CuratedItineraryRelease
{
    public const string ReviewedV1ManifestSha256 =
        "8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43";
    public const string SaintPetersburgV1ManifestSha256 =
        "3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d";
    public static CuratedItineraryRelease ReviewedV1 { get; } =
        new("moscow-outdoor", "1", ReviewedV1ManifestSha256);
    public static CuratedItineraryRelease ReviewedSaintPetersburgV1 { get; } =
        new("saint-petersburg-outdoor", "1", SaintPetersburgV1ManifestSha256);
    public string DatasetId { get; }
    public string Version { get; }
    public string ManifestSha256 { get; }
    public bool IsSaintPetersburg => DatasetId == "saint-petersburg-outdoor";
    public string DestinationId => IsSaintPetersburg ? "saint-petersburg" : "moscow";
    public string PayloadFile => DestinationId + "-v1.json";
    public string ManifestFile => DestinationId + "-v1.manifest.json";
    public string SourceUrl =>
        IsSaintPetersburg
            ? "https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz"
            : "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz";
    public string ProfileVersion => IsSaintPetersburg ? "spb-outdoor-profile-v1" : "profile-v1";
    public decimal[] Bounds =>
        IsSaintPetersburg ? [59.923m, 30.290m, 59.947m, 30.346m] : [55.72m, 37.57m, 55.78m, 37.66m];

    public CuratedItineraryRelease(string datasetId, string version, string manifestSha256)
        : this(datasetId, version, manifestSha256, false) { }

    // Explicit offline candidate identity supplied by the operator. Runtime composition
    // uses only the reviewed descriptors above. Moscow v1 can never be repinned.
    public static CuratedItineraryRelease ForCandidateVerification(
        string datasetId,
        string version,
        string manifestSha256
    ) => new(datasetId, version, manifestSha256, datasetId == "saint-petersburg-outdoor");

    private CuratedItineraryRelease(
        string datasetId,
        string version,
        string manifestSha256,
        bool initialCandidate
    )
    {
        if (datasetId is not ("moscow-outdoor" or "saint-petersburg-outdoor"))
            throw new ArgumentException(
                "Only the declared Moscow and Saint Petersburg outdoor datasets are supported.",
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
            || (
                !initialCandidate
                && version == "1"
                && manifestSha256
                    != (
                        datasetId == "moscow-outdoor"
                            ? ReviewedV1ManifestSha256
                            : SaintPetersburgV1ManifestSha256
                    )
            )
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
