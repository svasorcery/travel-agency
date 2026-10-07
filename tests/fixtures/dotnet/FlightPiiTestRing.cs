using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Shared.Abstractions;
using Xunit;

[assembly: AssemblyFixture(typeof(Travel.Tests.Fixtures.FlightPiiTestFixture))]

namespace Travel.Tests.Fixtures;

public sealed class FlightPiiTestFixture : IDisposable
{
    private readonly FlightPiiTestRing ring = new();

    public FlightPiiTestFixture() => TestPii.Ring = ring;

    public void Dispose()
    {
        TestPii.Ring = null;
        ring.Dispose();
    }
}

public static class TestPii
{
    internal static FlightPiiTestRing? Ring { get; set; }
    public static DataProtectionBookingPassengerProtector Protector =>
        Ring?.Protector ?? throw new InvalidOperationException("Test ring unavailable.");
    public static FlightsPiiProtectionProvider Provider =>
        Ring?.Crypto ?? throw new InvalidOperationException("Test ring unavailable.");
    public static ProtectedWebhookPayloadCodec Webhooks => new(Provider);
    public static DuffelWebhookPayloadReader WebhookReader => new(Webhooks);
    public static Dictionary<string, string?> Configuration =>
        Ring?.Configuration ?? throw new InvalidOperationException("Test ring unavailable.");

    public static DataProtectionBookingPassengerPartyProtector PartyProtector => new(Provider);

    public static QuoteBinding Binding(int count = 1, DateOnly? firstDeparture = null) =>
        QuoteBinding
            .Create(
                Guid.Parse("00000000-0000-0000-0000-000000000111"),
                BookableOfferParty
                    .Create(
                        Enumerable
                            .Range(1, count)
                            .Select(i => new SupplierPassengerSlot(
                                SupplierPassengerReference.Create($"pas_{i}").Value,
                                BookingPassengerKind.Adult
                            )),
                        firstDeparture ?? new DateOnly(2027, 1, 1),
                        true,
                        false
                    )
                    .Value,
                Enumerable
                    .Range(1, count)
                    .Select(i => new QuotePassengerSlot(
                        BookingPassengerId
                            .Create(Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"))
                            .Value,
                        SupplierPassengerReference.Create($"pas_{i}").Value,
                        BookingPassengerKind.Adult
                    ))
            )
            .Value;

    public static EquatableArray<BookingPassenger> Passengers(
        QuoteBinding binding,
        PassengerInfo passenger
    ) =>
        new(
            binding
                .Slots.Select(slot =>
                    BookingPassenger
                        .Create(
                            slot.Id,
                            BookingPassengerDetails
                                .Create(passenger, PassengerTitle.Create("mr").Value)
                                .Value
                        )
                        .Value
                )
                .ToArray()
        );

    public static HoldOfferCommand HoldCommand(
        Guid AggregateId,
        Guid UserId,
        PassengerInfo Passenger,
        QuoteBinding Binding
    )
    {
        var snapshot =
            AggregateId == Guid.Empty || UserId == Guid.Empty
                ? ProtectedPassengerPartySnapshot.Create(1, "invalid-id-test").Value
                : PartyProtector
                    .Protect(
                        new(AggregateId, UserId, Binding.Revision, Binding.Party.PassengerCount),
                        Passengers(Binding, Passenger)
                    )
                    .Value;
        return new(
            AggregateId,
            UserId,
            Binding.Revision,
            Binding.Party.PassengerCount,
            snapshot,
            Guid.NewGuid(),
            new string('a', 64)
        );
    }

    public static ProtectedPassengerSnapshot Protect(
        Guid booking,
        Guid owner,
        PassengerInfo passenger
    ) =>
        booking == Guid.Empty || owner == Guid.Empty
            ? ProtectedPassengerSnapshot.Create(1, "invalid-id-test").Value
            : Protector.Protect(booking, owner, passenger).Value;
}

public sealed class FlightPiiTestRing : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "travel-pii-tests-" + Guid.NewGuid().ToString("N"));
    public FlightsPiiProtectionOptions Options { get; }
    public FlightsPiiProtectionProvider Crypto { get; }
    public DataProtectionBookingPassengerProtector Protector { get; }

    public FlightPiiTestRing(bool initialize = true)
    {
        Directory.CreateDirectory(Root);
        var certificate = CreateCertificate("active");
        Options = new()
        {
            KeyRingPath = Path.Combine(Root, "keys"),
            ActiveCertificatePath = certificate.Path,
            ActiveCertificatePassword = certificate.Password,
        };
        Crypto = NewProvider();
        Protector = new(Crypto);
        if (initialize && Crypto.Initialize().IsError)
            throw new InvalidOperationException("Test key initialization failed.");
    }

    public (string Path, string Password) CreateCertificate(string name)
    {
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
        var path = Path.Combine(Root, name + ".pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return (path, password);
    }

    public FlightsPiiProtectionProvider NewProvider() =>
        new(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);

    public Dictionary<string, string?> Configuration =>
        new()
        {
            ["Flights:PiiProtection:KeyRingPath"] = Options.KeyRingPath,
            ["Flights:PiiProtection:ActiveCertificatePath"] = Options.ActiveCertificatePath,
            ["Flights:PiiProtection:ActiveCertificatePassword"] = Options.ActiveCertificatePassword,
        };

    public void RotateKey()
    {
        using var cert = X509CertificateLoader.LoadPkcs12FromFile(
            Options.ActiveCertificatePath!,
            Options.ActiveCertificatePassword,
            X509KeyStorageFlags.EphemeralKeySet
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddDataProtection()
            .SetApplicationName(FlightsPiiProtectionProvider.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(Options.KeyRingPath!))
            .ProtectKeysWithCertificate(cert);
        using var scope = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        scope.GetRequiredService<IKeyManager>().CreateNewKey(now.AddSeconds(-1), now.AddDays(90));
    }

    public void Dispose()
    {
        Crypto.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}
