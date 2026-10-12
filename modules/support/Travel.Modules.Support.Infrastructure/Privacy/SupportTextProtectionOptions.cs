namespace Travel.Modules.Support.Infrastructure.Privacy;

public sealed class SupportTextProtectionOptions
{
    public const string SectionName = "Support:TextProtection";
    public string? KeyRingPath { get; set; }
    public string? ActiveCertificatePath { get; set; }
    public string? ActiveCertificatePassword { get; set; }
    public List<SupportReadCertificateOptions> ReadCertificates { get; set; } = [];
}

public sealed class SupportReadCertificateOptions
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
}

public enum SupportProtectionAvailability
{
    NotConfigured,
    Unavailable,
    Ready,
}
