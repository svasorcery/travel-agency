using System.Text.Json;
using Shouldly;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Support.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class SupportTextProtectionTests
{
    private static readonly SupportTextBinding Binding = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        1
    );
    private const string Secret = "Fictional protected Support sentinel <script>alert(1)</script>";

    [Fact]
    public async Task Real_envelope_binds_owner_case_message_written_revision_and_rejects_tamper()
    {
        using var ring = new SupportTestRing();
        var result = await ring.Protector.ProtectAsync(
            Secret,
            Binding,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotContain(Secret);
        File.ReadAllText(Directory.GetFiles(ring.Options.KeyRingPath!, "key-*.xml").Single())
            .ShouldContain("encryptedSecret");
        (
            await ring.Protector.UnprotectAsync(
                result.Value,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).Value.ShouldBe(Secret);
        foreach (
            var wrong in new[]
            {
                Binding with
                {
                    OwnerId = Guid.NewGuid(),
                },
                Binding with
                {
                    CaseId = Guid.NewGuid(),
                },
                Binding with
                {
                    MessageId = Guid.NewGuid(),
                },
                Binding with
                {
                    MessageId = null,
                },
                Binding with
                {
                    WrittenAtRevision = 2,
                },
            }
        )
            (
                await ring.Protector.UnprotectAsync(
                    result.Value,
                    wrong,
                    TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeTrue();
        using var parsed = JsonDocument.Parse(result.Value);
        var cipher = Convert.FromBase64String(
            parsed.RootElement.GetProperty("Ciphertext").GetString()!
        );
        cipher[^1] ^= 1;
        var tampered = JsonSerializer.Serialize(
            new { Format = 1, Ciphertext = Convert.ToBase64String(cipher) }
        );
        (
            await ring.Protector.UnprotectAsync(
                tampered,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Restart_and_rotated_certificate_keep_old_ciphertext_readable_only_with_retained_reader()
    {
        using var ring = new SupportTestRing();
        var original = (
            await ring.Protector.ProtectAsync(
                Secret,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).Value;
        using (var restarted = ring.NewProvider())
            (
                await new DataProtectionSupportTextProtector(restarted).UnprotectAsync(
                    original,
                    Binding,
                    TestContext.Current.CancellationToken
                )
            ).Value.ShouldBe(Secret);
        ring.RotateCertificate(true);
        using (var rotated = ring.NewProvider())
        {
            var protector = new DataProtectionSupportTextProtector(rotated);
            (
                await protector.UnprotectAsync(
                    original,
                    Binding,
                    TestContext.Current.CancellationToken
                )
            ).Value.ShouldBe(Secret);
            var newCipher = await protector.ProtectAsync(
                "New fictional text",
                Binding,
                TestContext.Current.CancellationToken
            );
            newCipher.IsError.ShouldBeFalse();
        }
        ring.Options.ReadCertificates.Clear();
        using var missingReader = ring.NewProvider();
        (
            await new DataProtectionSupportTextProtector(missingReader).UnprotectAsync(
                original,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Ordinary_protection_does_not_create_keys_and_missing_key_does_not_return_plaintext()
    {
        using var empty = new SupportTestRing(false);
        (
            await empty.Protector.ProtectAsync(
                Secret,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
        Directory.Exists(empty.Options.KeyRingPath).ShouldBeFalse();
        empty.Provider.Availability.ShouldBe(SupportProtectionAvailability.Unavailable);
        using var ring = new SupportTestRing();
        var original = (
            await ring.Protector.ProtectAsync(
                Secret,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).Value;
        foreach (var file in Directory.GetFiles(ring.Options.KeyRingPath!, "*.xml"))
            File.Delete(file);
        using var restarted = ring.NewProvider();
        (
            await new DataProtectionSupportTextProtector(restarted).UnprotectAsync(
                original,
                Binding,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
        Directory.GetFiles(ring.Options.KeyRingPath!, "*.xml").ShouldBeEmpty();
    }
}
