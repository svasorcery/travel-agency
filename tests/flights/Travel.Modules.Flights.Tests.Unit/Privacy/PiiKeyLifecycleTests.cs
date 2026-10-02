using System.Text;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class PiiKeyLifecycleTests
{
    [Fact]
    public void Rotated_keys_and_certificates_retain_readability_and_restore_from_backup()
    {
        using var ring = new FlightPiiTestRing();
        var payload = Encoding.UTF8.GetBytes("fictional-secret");
        var first = ring.Crypto.Protect(payload, "fixture").Value;
        var oldPath = ring.Options.ActiveCertificatePath!;
        var oldPassword = ring.Options.ActiveCertificatePassword;
        var next = ring.CreateCertificate("next");
        ring.Options.ActiveCertificatePath = next.Path;
        ring.Options.ActiveCertificatePassword = next.Password;
        ring.Options.ReadCertificates.Add(new() { Path = oldPath, Password = oldPassword });
        ring.RotateKey();
        byte[] second;
        using (var rotated = ring.NewProvider())
        {
            rotated.Unprotect(first, "fixture").Value.ShouldBe(payload);
            second = rotated.Protect(payload, "fixture").Value;
        }
        var backup = Path.Combine(ring.Root, "backup");
        Directory.CreateDirectory(backup);
        foreach (var path in Directory.GetFiles(ring.Options.KeyRingPath!, "*.xml"))
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
        Directory.Delete(ring.Options.KeyRingPath!, true);
        using (var lost = ring.NewProvider())
        {
            lost.Unprotect(first, "fixture").IsError.ShouldBeTrue();
            lost.Protect(payload, "fixture").IsError.ShouldBeTrue();
            Directory.Exists(ring.Options.KeyRingPath).ShouldBeFalse();
        }
        ring.Options.KeyRingPath = backup;
        using (var restored = ring.NewProvider())
        {
            restored.Unprotect(first, "fixture").Value.ShouldBe(payload);
            restored.Unprotect(second, "fixture").Value.ShouldBe(payload);
        }
        ring.Options.ReadCertificates.Clear();
        using var incompleteRestore = ring.NewProvider();
        incompleteRestore.Unprotect(first, "fixture").IsError.ShouldBeTrue();
        incompleteRestore.Unprotect(second, "fixture").Value.ShouldBe(payload);
    }
}
