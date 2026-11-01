using System.Security.Cryptography;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2BuildInventoryTests
{
    [Theory]
    [InlineData("changed-dll")]
    [InlineData("extra-runtimeconfig")]
    [InlineData("escaped-file")]
    public void Frozen_inventory_rejects_changed_bytes_inventory_or_escape(string fault)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "hotels-m2-build-test-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "owned.dll");
            File.WriteAllText(file, "original");
            var files = new Dictionary<string, string>
            {
                { "owned.dll", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) },
            };
            HotelM2BuildInventory.VerifyDirectory(directory, files);
            if (fault == "changed-dll")
                File.WriteAllText(file, "changed");
            if (fault == "extra-runtimeconfig")
                File.WriteAllText(Path.Combine(directory, "extra.runtimeconfig.json"), "{}");
            if (fault == "escaped-file")
            {
                files.Clear();
                files.Add(Path.Combine(directory, "owned.dll"), "invalid");
            }
            Should.Throw<InvalidDataException>(() =>
                HotelM2BuildInventory.VerifyDirectory(directory, files)
            );
        }
        finally
        {
            foreach (var f in Directory.GetFiles(directory))
                File.Delete(f);
            Directory.Delete(directory);
        }
    }
}
