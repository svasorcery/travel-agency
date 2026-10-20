namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class TripsProtectionOptions
{
    public const string SectionName = "Trips:Protection";
    public string? KeyRingPath { get; set; }
    public string? ActiveCertificatePath { get; set; }
    public string? ActiveCertificatePassword { get; set; }
    public TripsReadCertificate[] ReadCertificates { get; set; } = [];
}

public sealed class TripsReadCertificate
{
    public string Path { get; set; } = "";
    public string? Password { get; set; }
}
