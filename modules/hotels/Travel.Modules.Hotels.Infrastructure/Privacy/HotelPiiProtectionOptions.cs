namespace Travel.Modules.Hotels.Infrastructure.Privacy;

public sealed class HotelPiiProtectionOptions
{
    public const string SectionName = "Hotels:PiiProtection";
    public string? KeyRingPath { get; set; }
    public string? ActiveCertificatePath { get; set; }
    public string? ActiveCertificatePassword { get; set; }
    public List<HotelReadCertificateOptions> ReadCertificates { get; set; } = [];
}

public sealed class HotelReadCertificateOptions
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
}

public enum HotelProtectionAvailability
{
    NotConfigured,
    Unavailable,
    Ready,
}
