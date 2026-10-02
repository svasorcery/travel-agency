using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Privacy;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class PiiProtectionTests
{
    [Fact]
    public void Empty_ring_is_not_bootstrapped_by_an_ordinary_write()
    {
        using var fixture = new TestRing();
        using var provider = fixture.Provider();
        provider.Protect([1, 2], "test").IsError.ShouldBeTrue();
        Directory.Exists(fixture.Options.KeyRingPath).ShouldBeFalse();
    }

    [Fact]
    public void Explicit_initialization_protects_and_binds_snapshots_across_restart()
    {
        using var fixture = new TestRing();
        var booking = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var passenger = PassengerInfo
            .Create(
                "FictionalGiven",
                "FictionalFamily",
                new DateOnly(1987, 2, 14),
                Gender.Female,
                "fictional-pii@example.test",
                PhoneNumber.Create("+12025550123").Value,
                new DateOnly(2030, 1, 1)
            )
            .Value;
        string serialized;
        ProtectedPassengerSnapshot snapshot;
        using (var provider = fixture.Provider())
        {
            provider.Initialize().IsError.ShouldBeFalse();
            provider.Initialize().IsError.ShouldBeTrue();
            var protector = new DataProtectionBookingPassengerProtector(provider);
            snapshot = protector.Protect(booking, owner, passenger).Value;
            serialized = JsonSerializer.Serialize(snapshot);
            foreach (
                var sentinel in new[]
                {
                    "FictionalGiven",
                    "FictionalFamily",
                    "1987-02-14",
                    "fictional-pii@example.test",
                    "+12025550123",
                }
            )
                serialized.ShouldNotContain(sentinel);
            protector
                .Protect(booking, owner, passenger)
                .Value.Ciphertext.ShouldNotBe(snapshot.Ciphertext);
            protector.Unprotect(booking, Guid.NewGuid(), snapshot).IsError.ShouldBeTrue();
            protector.Unprotect(Guid.NewGuid(), owner, snapshot).IsError.ShouldBeTrue();
        }
        using var restarted = fixture.Provider();
        var read = new DataProtectionBookingPassengerProtector(restarted);
        read.Unprotect(
                booking,
                owner,
                JsonSerializer.Deserialize<ProtectedPassengerSnapshot>(serialized)!
            )
            .Value.ShouldBe(passenger);
        var bytes = Convert.FromBase64String(snapshot.Ciphertext);
        bytes[^1] ^= 1;
        read.Unprotect(
                booking,
                owner,
                ProtectedPassengerSnapshot.Create(1, Convert.ToBase64String(bytes)).Value
            )
            .IsError.ShouldBeTrue();
        var keyText = string.Join(
            "",
            Directory.GetFiles(fixture.Options.KeyRingPath!, "*.xml").Select(File.ReadAllText)
        );
        keyText.ShouldContain("encryptedSecret");
    }

    [Theory]
    [InlineData(
        "{\"givenName\":\"Fixture\",\"familyName\":\"Person\",\"email\":\"not-an-email\",\"phone\":{\"value\":\"bad\"},\"gender\":{\"code\":\"bad\"}}"
    )]
    [InlineData(
        "{\"givenName\":\"Fixture\",\"familyName\":\"Person\",\"email\":\"fixture@example.test\",\"phone\":{\"value\":null},\"gender\":{\"code\":\"male\"}}"
    )]
    public void Invalid_decrypted_shapes_are_refused_with_safe_errors(string json)
    {
        var booking = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var encrypted = Travel
            .Tests.Fixtures.TestPii.Provider.Protect(
                System.Text.Encoding.UTF8.GetBytes(json),
                "booking-passenger.v1",
                booking.ToString("N"),
                owner.ToString("N")
            )
            .Value;
        var result = Travel.Tests.Fixtures.TestPii.Protector.Unprotect(
            booking,
            owner,
            ProtectedPassengerSnapshot.Create(1, Convert.ToBase64String(encrypted)).Value
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
    }

    private sealed class TestRing : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            "travel-pii-tests-" + Guid.NewGuid().ToString("N")
        );
        public FlightsPiiProtectionOptions Options { get; }

        public TestRing()
        {
            Directory.CreateDirectory(root);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=Fictional PII Test",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using var cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(7)
            );
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var path = Path.Combine(root, "test.pfx");
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
            Options = new()
            {
                KeyRingPath = Path.Combine(root, "keys"),
                ActiveCertificatePath = path,
                ActiveCertificatePassword = password,
            };
        }

        public FlightsPiiProtectionProvider Provider() =>
            new(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

        public void Dispose() => Directory.Delete(root, true);
    }
}
