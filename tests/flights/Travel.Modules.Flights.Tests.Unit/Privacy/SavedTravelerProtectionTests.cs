using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class SavedTravelerProtectionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Profile_roundtrips_across_restart_and_is_bound_to_owner_traveler_revision_and_purpose()
    {
        using var ring = new FlightPiiTestRing();
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        var context = Context();
        var details = Details();
        var protectedResult = protector.Protect(context, details);
        protectedResult.IsError.ShouldBeFalse();
        var snapshot = protectedResult.Value;
        var json = JsonSerializer.Serialize(snapshot);
        foreach (
            var sentinel in new[]
            {
                "FictionalGiven",
                "FictionalFamily",
                "1987-02-14",
                "fictional-pii@example.test",
                "12025550123",
            }
        )
            json.ShouldNotContain(sentinel);
        snapshot.ToString().ShouldBe("ProtectedSavedTravelerSnapshot");
        protector.Protect(context, details).Value.Ciphertext.ShouldNotBe(snapshot.Ciphertext);
        foreach (
            var wrong in new[]
            {
                context with
                {
                    OwnerUserId = Guid.NewGuid(),
                },
                context with
                {
                    TravelerId = Guid.NewGuid(),
                },
                context with
                {
                    Revision = Guid.NewGuid(),
                },
            }
        )
            SafeFailure(protector.Unprotect(wrong, snapshot));
        using var restarted = ring.NewProvider();
        var reader = new DataProtectionSavedTravelerProtector(restarted);
        reader
            .Unprotect(context, JsonSerializer.Deserialize<ProtectedSavedTravelerSnapshot>(json)!)
            .Value.ShouldBe(details);
        // A fixture-produced payload proves the exact public purpose chain, not only roundtrip.
        reader
            .Unprotect(
                context,
                Encrypt(ring, context, JsonSerializer.Serialize(details, JsonOptions))
            )
            .Value.ShouldBe(details);
        var bytes = Convert.FromBase64String(snapshot.Ciphertext);
        bytes[^1] ^= 1;
        SafeFailure(
            reader.Unprotect(
                context,
                ProtectedSavedTravelerSnapshot.Create(1, Convert.ToBase64String(bytes)).Value
            )
        );
        var wrongPurpose = ring
            .Crypto.Protect(
                JsonSerializer.SerializeToUtf8Bytes(details, JsonOptions),
                "booking-passenger.v1",
                context.OwnerUserId.ToString("N"),
                context.TravelerId.ToString("N"),
                context.Revision.ToString("N")
            )
            .Value;
        SafeFailure(
            reader.Unprotect(
                context,
                ProtectedSavedTravelerSnapshot.Create(1, Convert.ToBase64String(wrongPurpose)).Value
            )
        );
    }

    [Fact]
    public void All_context_ids_and_valid_details_are_required_on_protect_and_unprotect()
    {
        using var ring = new FlightPiiTestRing();
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        var context = Context();
        var snapshot = protector.Protect(context, Details()).Value;
        foreach (
            var invalid in new[]
            {
                null!,
                context with
                {
                    OwnerUserId = Guid.Empty,
                },
                context with
                {
                    TravelerId = Guid.Empty,
                },
                context with
                {
                    Revision = Guid.Empty,
                },
            }
        )
        {
            protector
                .Protect(invalid, Details())
                .FirstError.Code.ShouldBe("Flights.PiiEnvelopeInvalid");
            SafeFailure(protector.Unprotect(invalid, snapshot));
        }
        protector.Protect(context, null!).FirstError.Code.ShouldBe("Flights.PiiEnvelopeInvalid");
        var malformed = JsonSerializer.Deserialize<SavedTravelerDetails>(
            "{\"passenger\":null,\"title\":null}",
            JsonOptions
        )!;
        protector
            .Protect(context, malformed)
            .FirstError.Code.ShouldBe("Flights.PiiEnvelopeInvalid");
        SafeFailure(protector.Unprotect(context, null!));
    }

    [Fact]
    public void Unsupported_format_invalid_encoding_and_oversized_corrupt_rows_fail_safely()
    {
        using var ring = new FlightPiiTestRing();
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        var context = Context();
        var snapshot = protector.Protect(context, Details()).Value;
        ProtectedSavedTravelerSnapshot.Create(2, snapshot.Ciphertext).IsError.ShouldBeTrue();
        ProtectedSavedTravelerSnapshot.Create(1, " ").IsError.ShouldBeTrue();
        ProtectedSavedTravelerSnapshot.Create(1, new string('a', 32769)).IsError.ShouldBeTrue();
        ProtectedSavedTravelerSnapshot.Create(1, new string('a', 32768)).IsError.ShouldBeFalse();
        foreach (
            var row in new[]
            {
                new { FormatVersion = 2, Ciphertext = snapshot.Ciphertext },
                new { FormatVersion = 1, Ciphertext = "raw-sentinel@example.test" },
                new { FormatVersion = 1, Ciphertext = new string('a', 32769) },
                new { FormatVersion = 1, Ciphertext = (string)null! },
            }
        )
        {
            var corrupt = JsonSerializer.Deserialize<ProtectedSavedTravelerSnapshot>(
                JsonSerializer.Serialize(row, JsonOptions),
                JsonOptions
            )!;
            SafeFailure(protector.Unprotect(context, corrupt));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"passenger\":null,\"title\":null}")]
    [InlineData("{\"passenger\":{\"givenName\":\"Fixture\"},\"title\":{\"code\":\"mr\"}}")]
    public void Decrypted_invalid_shapes_return_safe_errors(string json)
    {
        using var ring = new FlightPiiTestRing();
        var context = Context();
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        SafeFailure(protector.Unprotect(context, Encrypt(ring, context, json)));
    }

    [Theory]
    [InlineData("male", "raw-sentinel@example.test")]
    [InlineData("mr", "MR")]
    [InlineData("1987-02-14", "0001-01-01")]
    [InlineData("fictional-pii@example.test", " invalid-email")]
    [InlineData("FictionalGiven", "Иван")]
    [InlineData("+12025550123", "+12025550٦23")]
    public void Decrypted_details_are_revalidated(string original, string replacement)
    {
        using var ring = new FlightPiiTestRing();
        var context = Context();
        var json = JsonSerializer
            .Serialize(Details(), JsonOptions)
            .Replace(
                JsonSerializer.Serialize(original, JsonOptions),
                JsonSerializer.Serialize(replacement, JsonOptions)
            );
        var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
        SafeFailure(protector.Unprotect(context, Encrypt(ring, context, json)));
    }

    [Fact]
    public void Retained_old_keys_and_certificates_can_read_profile_after_rotation()
    {
        using var ring = new FlightPiiTestRing();
        var context = Context();
        var snapshot = new DataProtectionSavedTravelerProtector(ring.Crypto)
            .Protect(context, Details())
            .Value;
        var oldPath = ring.Options.ActiveCertificatePath!;
        var oldPassword = ring.Options.ActiveCertificatePassword;
        var next = ring.CreateCertificate("next");
        ring.Options.ActiveCertificatePath = next.Path;
        ring.Options.ActiveCertificatePassword = next.Password;
        ring.Options.ReadCertificates.Add(new() { Path = oldPath, Password = oldPassword });
        ring.RotateKey();
        using (var rotated = ring.NewProvider())
            new DataProtectionSavedTravelerProtector(rotated)
                .Unprotect(context, snapshot)
                .Value.ShouldBe(Details());
        ring.Options.ReadCertificates.Clear();
        using var withoutOldCertificate = ring.NewProvider();
        SafeFailure(
            new DataProtectionSavedTravelerProtector(withoutOldCertificate).Unprotect(
                context,
                snapshot
            )
        );
    }

    [Fact]
    public void Empty_ring_is_not_provisioned_by_profile_write()
    {
        using var ring = new FlightPiiTestRing(initialize: false);
        var result = new DataProtectionSavedTravelerProtector(ring.Crypto).Protect(
            Context(),
            Details()
        );
        result.FirstError.Code.ShouldBe("Flights.PiiProtectionUnavailable");
        Directory.Exists(ring.Options.KeyRingPath).ShouldBeFalse();
    }

    [Fact]
    public void Plaintext_byte_buffers_are_cleared_after_protect_unprotect_and_invalid_shape()
    {
        using var ring = new FlightPiiTestRing();
        // The existing provider is sealed; a test-only forwarding decorator observes real crypto
        // buffers without introducing production seams or changing the shared fixture.
        var field = typeof(FlightsPiiProtectionProvider).GetField(
            "services",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var original = (ServiceProvider)field.GetValue(ring.Crypto)!;
        var capture = new CapturingProvider(original.GetRequiredService<IDataProtectionProvider>());
        using var observed = new ServiceCollection()
            .AddSingleton(capture)
            .AddSingleton<IDataProtectionProvider>(capture)
            .AddSingleton(original.GetRequiredService<IKeyManager>())
            .BuildServiceProvider();
        field.SetValue(ring.Crypto, observed);
        try
        {
            var context = Context();
            var protector = new DataProtectionSavedTravelerProtector(ring.Crypto);
            var snapshot = protector.Protect(context, Details()).Value;
            capture.ProtectedPlaintext!.Length.ShouldBeGreaterThan(0);
            capture.ProtectedPlaintext.ShouldAllBe(b => b == 0);
            protector.Unprotect(context, snapshot).Value.ShouldBe(Details());
            capture.UnprotectedPlaintext!.ShouldAllBe(b => b == 0);
            SafeFailure(protector.Unprotect(context, Encrypt(ring, context, "null")));
            capture.UnprotectedPlaintext!.ShouldAllBe(b => b == 0);
            SafeFailure(protector.Unprotect(context, Encrypt(ring, context, "{")));
            capture.UnprotectedPlaintext!.ShouldAllBe(b => b == 0);
        }
        finally
        {
            field.SetValue(ring.Crypto, original);
        }
    }

    private static SavedTravelerProtectionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    private static SavedTravelerDetails Details() =>
        SavedTravelerDetails
            .CreateRaw(
                "mr",
                "FictionalGiven",
                "FictionalFamily",
                new(1987, 2, 14),
                "male",
                "fictional-pii@example.test",
                "+12025550123",
                new(2026, 10, 3)
            )
            .Value;

    private static ProtectedSavedTravelerSnapshot Encrypt(
        FlightPiiTestRing ring,
        SavedTravelerProtectionContext context,
        string json
    )
    {
        var bytes = ring
            .Crypto.Protect(
                Encoding.UTF8.GetBytes(json),
                "saved-traveler.v1",
                context.OwnerUserId.ToString("N"),
                context.TravelerId.ToString("N"),
                context.Revision.ToString("N")
            )
            .Value;
        return ProtectedSavedTravelerSnapshot.Create(1, Convert.ToBase64String(bytes)).Value;
    }

    private static void SafeFailure(ErrorOr.ErrorOr<SavedTravelerDetails> result)
    {
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        result.FirstError.Description.ShouldNotContain("raw-sentinel");
        result.FirstError.Metadata.ShouldBeNull();
    }

    private sealed class CapturingProvider(IDataProtectionProvider inner) : IDataProtectionProvider
    {
        public byte[]? ProtectedPlaintext { get; set; }
        public byte[]? UnprotectedPlaintext { get; set; }

        public IDataProtector CreateProtector(string purpose) =>
            new CapturingProtector(inner.CreateProtector(purpose), this);

        private sealed class CapturingProtector(IDataProtector inner, CapturingProvider owner)
            : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) =>
                new CapturingProtector(inner.CreateProtector(purpose), owner);

            public byte[] Protect(byte[] plaintext)
            {
                owner.ProtectedPlaintext = plaintext;
                return inner.Protect(plaintext);
            }

            public byte[] Unprotect(byte[] protectedData)
            {
                var plaintext = inner.Unprotect(protectedData);
                owner.UnprotectedPlaintext = plaintext;
                return plaintext;
            }
        }
    }
}
