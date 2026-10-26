using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsLegacyCodecTests
{
    [Theory]
    [InlineData("moscow", "93d0ba8a6dd36c565ecb897973ffb9b92e166bb2a54beddd4565f0381749f2f8")]
    [InlineData(
        "saint-petersburg",
        "3ded6bf53240a3a031a74492380469e24d921bb313e1f85c21df7ce769e3f717"
    )]
    public async Task Base_codec_plaintext_preserves_full_detail_and_current_protection_binding(
        string city,
        string expectedHash
    )
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        var directory = Path.Combine(root!.FullName, "tests/fixtures/trips-m1-legacy-codec");
        var bytes = await File.ReadAllBytesAsync(
            Path.Combine(directory, "legacy-" + city + ".detail.json"),
            TestContext.Current.CancellationToken
        );
        Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(expectedHash);
        using var document = JsonDocument.Parse(bytes);
        var value = document.RootElement;
        using var provenance = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                Path.Combine(directory, "provenance.json"),
                TestContext.Current.CancellationToken
            )
        );
        provenance
            .RootElement.GetProperty("baseSha")
            .GetString()
            .ShouldBe("11ba5a1572e53cd901ae4ec56b77b482ec95f699");
        var owner = TripsCodecProtectionTests.Owner;
        var row = new TripPlanRecord(
            value.GetProperty("tripId").GetGuid(),
            owner,
            7,
            DateTimeOffset.Parse(
                value.GetProperty("createdAt").GetString()!,
                CultureInfo.InvariantCulture
            ),
            DateTimeOffset.Parse(
                value.GetProperty("updatedAt").GetString()!,
                CultureInfo.InvariantCulture
            ),
            "opaque"
        );
        var text = Encoding.UTF8.GetString(bytes);
        var codec = new TripPlanJsonCodec();
        var decoded = codec.Deserialize(text, row);
        decoded.IsError.ShouldBeFalse();
        var plan = decoded.Value;
        codec.Serialize(plan).Value.ShouldBe(text);
        codec.MeasureDetailBytes(plan, false).Value.ShouldBe(bytes.Length);
        plan.Owner.ShouldBe(owner);
        plan.Revision.ShouldBe(7);
        plan.Destination.DisplayName.ShouldBe("Историческое имя " + city);
        plan.Destination.CatalogueVersion.ShouldBe("1");
        plan.Destination.TimeZone.ShouldBe("Europe/Moscow");
        plan.Days.SelectMany(d => d.Blocks)
            .Select(b => b.BlockId)
            .ShouldBe(
                new[]
                {
                    Guid.Parse("33333333-3333-4333-8333-333333333333"),
                    Guid.Parse("44444444-4444-4444-8444-444444444444"),
                    Guid.Parse("55555555-5555-4555-8555-555555555555"),
                }
            );
        value.GetProperty("knownUserEstimateSubtotal").GetString().ShouldBe("123.55");
        value.GetProperty("unknownEstimateCount").GetInt32().ShouldBe(1);
        codec.Deserialize(text, row with { Revision = 8 }).IsError.ShouldBeTrue();
        codec
            .Deserialize(text, row with { UpdatedAt = row.UpdatedAt.AddTicks(1) })
            .IsError.ShouldBeTrue();
        codec.Deserialize(text, row with { TripId = Guid.NewGuid() }).IsError.ShouldBeTrue();
        using var ring = new TripsTestRing();
        var binding = new TripPlanBinding(owner, row.TripId, row.Revision);
        var encrypted = await ring.Protector.ProtectAsync(
            text,
            binding,
            TestContext.Current.CancellationToken
        );
        encrypted.IsError.ShouldBeFalse();
        var keys = KeyHashes(ring.Settings.KeyRingPath!);
        using var restarted = new TripsProtectionProvider(
            Options.Create(ring.Settings),
            TimeProvider.System
        );
        var protector = new DataProtectionTripPlanProtector(restarted);
        var read = await protector.UnprotectAsync(
            encrypted.Value,
            binding,
            TestContext.Current.CancellationToken
        );
        read.IsError.ShouldBeFalse();
        read.Value.ShouldBe(text);
        codec.Serialize(codec.Deserialize(read.Value, row).Value).Value.ShouldBe(text);
        KeyHashes(ring.Settings.KeyRingPath!).ShouldBe(keys);
        foreach (
            var wrong in new[]
            {
                binding with
                {
                    Revision = 8,
                },
                binding with
                {
                    TripId = Guid.NewGuid(),
                },
                binding with
                {
                    Owner = OwnerIdentity.Create(owner.Issuer, Guid.NewGuid()).Value,
                },
                binding with
                {
                    Owner = OwnerIdentity
                        .Create("https://foreign.fictional.test", owner.Subject)
                        .Value,
                },
            }
        )
            (
                await protector.UnprotectAsync(
                    encrypted.Value,
                    wrong,
                    TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeTrue();
    }

    private static string[] KeyHashes(string directory) =>
        Directory
            .GetFiles(directory)
            .Order()
            .Select(path =>
                Path.GetFileName(path)
                + ":"
                + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))
            )
            .ToArray();
}
