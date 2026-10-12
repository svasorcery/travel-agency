using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Travel.Modules.Support.Infrastructure.Privacy;

namespace Travel.Modules.Support.Tests.Integration;

// Owned disposable CI test artifacts only. This fixture is never production provisioning.
public sealed class SupportTestRing : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "travel-support-tests-" + Guid.NewGuid().ToString("N"));
    public SupportTextProtectionOptions Options { get; }
    public SupportTextProtectionProvider Provider { get; }
    public DataProtectionSupportTextProtector Protector { get; }

    public SupportTestRing(bool initialize = true)
    {
        Directory.CreateDirectory(Root);
        var cert = Certificate("active");
        Options = new()
        {
            KeyRingPath = Path.Combine(Root, "keys"),
            ActiveCertificatePath = cert.Path,
            ActiveCertificatePassword = cert.Password,
        };
        Provider = NewProvider();
        Protector = new(Provider);
        if (initialize && Provider.Initialize().IsError)
            throw new InvalidOperationException("Test key initialization failed.");
    }

    public (string Path, string Password) Certificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Fictional Support CI",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(7)
        );
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var path = Path.Combine(Root, name + ".pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return (path, password);
    }

    public SupportTextProtectionProvider NewProvider() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

    public void RotateCertificate(bool retainOld)
    {
        var old = new SupportReadCertificateOptions
        {
            Path = Options.ActiveCertificatePath!,
            Password = Options.ActiveCertificatePassword,
        };
        var next = Certificate("rotation");
        Options.ActiveCertificatePath = next.Path;
        Options.ActiveCertificatePassword = next.Password;
        Options.ReadCertificates = retainOld ? [old] : [];
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            next.Path,
            next.Password,
            X509KeyStorageFlags.EphemeralKeySet
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddDataProtection()
            .SetApplicationName(SupportTextProtectionProvider.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(Options.KeyRingPath!))
            .ProtectKeysWithCertificate(certificate);
        using var scope = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        scope.GetRequiredService<IKeyManager>().CreateNewKey(now.AddSeconds(-1), now.AddDays(90));
    }

    public void Dispose()
    {
        Provider.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}
