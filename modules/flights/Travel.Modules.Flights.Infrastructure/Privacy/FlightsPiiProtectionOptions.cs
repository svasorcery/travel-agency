namespace Travel.Modules.Flights.Infrastructure.Privacy;

public sealed class FlightsPiiProtectionOptions
{
    public const string SectionName = "Flights:PiiProtection";
    public string? KeyRingPath { get; set; }
    public string? ActiveCertificatePath { get; set; }
    public string? ActiveCertificatePassword { get; set; }
    public List<PiiReadCertificateOptions> ReadCertificates { get; set; } = [];
}

public sealed class PiiReadCertificateOptions
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
}

public enum PiiProtectionAvailability
{
    NotConfigured,
    Unavailable,
    Ready,
}
