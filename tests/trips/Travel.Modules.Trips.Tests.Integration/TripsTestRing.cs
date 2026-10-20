using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Travel.Modules.Trips.Infrastructure.Privacy;

namespace Travel.Modules.Trips.Tests.Integration;

// Fictional test-owned certificate/ring only; not a runtime provisioning API.
internal sealed class TripsTestRing : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "travel-trips-fixture-" + Guid.NewGuid().ToString("N")
    );
    internal TripsProtectionOptions Settings { get; }
    internal TripsProtectionProvider Provider { get; }
    internal DataProtectionTripPlanProtector Protector { get; }

    internal TripsTestRing()
    {
        Directory.CreateDirectory(root);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Fictional Trips Source Tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7)
        );
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var certificatePath = Path.Combine(root, "fixture.pfx");
        File.WriteAllBytes(certificatePath, cert.Export(X509ContentType.Pfx, password));
        var ring = Path.Combine(root, "ring");
        Directory.CreateDirectory(ring);
        Settings = new()
        {
            KeyRingPath = ring,
            ActiveCertificatePath = certificatePath,
            ActiveCertificatePassword = password,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddDataProtection()
            .SetApplicationName(TripsProtectionProvider.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(ring))
            .ProtectKeysWithCertificate(cert);
        using (var container = services.BuildServiceProvider())
            container
                .GetRequiredService<IKeyManager>()
                .CreateNewKey(
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddDays(7)
                );
        Provider = new TripsProtectionProvider(Options.Create(Settings), TimeProvider.System);
        Protector = new(Provider);
    }

    public void Dispose()
    {
        Provider.Dispose();
        var resolved = Path.GetFullPath(root);
        var temporary = Path.GetFullPath(Path.GetTempPath());
        if (
            !resolved.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved)
                .StartsWith("travel-trips-fixture-", StringComparison.Ordinal)
        )
            throw new InvalidOperationException("Unexpected fixture path.");
        Directory.Delete(resolved, true);
    }
}
