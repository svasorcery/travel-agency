namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

public sealed class HotelsOptions
{
    public HotelAggregationOptions Aggregation { get; set; } = new();
    public bool Enabled { get; set; }
    public bool BookingEnabled { get; set; }
    public bool ReceiverEnabled { get; set; }
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string? OrganisationId { get; set; }
    public int ForegroundStarts { get; set; } = 24;
    public int RecoveryStarts { get; set; } = 6;
    public int ForegroundConcurrency { get; set; } = 1;
    public int RecoveryConcurrency { get; set; } = 1;
    public int WindowSeconds { get; set; } = 60;
    public int ReadTimeoutSeconds { get; set; } = 15;
    public int CreateTimeoutSeconds { get; set; } = 140;
    public int ResponseBytes { get; set; } = 2 * 1024 * 1024;
    public int Properties { get; set; } = 100;
    public int Rates { get; set; } = 200;
    public int CacheEntries { get; set; } = 128;
    public int CacheBytes { get; set; } = 16 * 1024 * 1024;

    public bool IsValid(bool production) =>
        (!production || !Enabled && !BookingEnabled && !ReceiverEnabled)
        && (
            !Enabled
            || ApiKey is { Length: >= 16 and <= 256 }
                && ApiKey.StartsWith("duffel_test_", StringComparison.Ordinal)
        )
        && (!BookingEnabled || Enabled && ReceiverEnabled)
        && (!ReceiverEnabled || WebhookSecret is { Length: >= 16 and <= 256 })
        && ForegroundStarts is >= 1 and <= 10000
        && RecoveryStarts is >= 1 and <= 10000
        && ForegroundConcurrency is >= 1 and <= 100
        && RecoveryConcurrency is >= 1 and <= 100
        && WindowSeconds is >= 1 and <= 3600
        && ReadTimeoutSeconds is >= 1 and <= 60
        && CreateTimeoutSeconds is >= 130 and <= 180
        && ResponseBytes is >= 1024 and <= 2 * 1024 * 1024
        && Properties is >= 1 and <= 100
        && Rates is >= 1 and <= 200
        && CacheEntries is >= 1 and <= 128
        && CacheBytes is >= 1024 and <= 16 * 1024 * 1024;
}

public sealed class HotelAggregationOptions
{
    public bool Enabled { get; set; }
    public bool ExternalUseApproved { get; set; }
    public bool Fictional { get; set; }
    public string AccountAlias { get; set; } = "duffel-test";
    public string ConfigurationRevision { get; set; } = "";
    public string ApprovalRevision { get; set; } = "";
    public string? ApprovedUseScope { get; set; }
    public bool RightsCleared =>
        Fictional
        || ExternalUseApproved
            && ApprovedUseScope == "DuffelHotelbedsSearchComparisonBooking"
            && ConfigurationRevision.Length is > 0 and <= 80
            && ApprovalRevision.Length is > 0 and <= 80;
}
