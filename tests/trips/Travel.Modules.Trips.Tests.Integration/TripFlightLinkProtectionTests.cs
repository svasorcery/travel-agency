using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripFlightLinkProtectionTests
{
    private readonly TripFlightLinkBinding binding = new(
        TripsCodecProtectionTests.Owner,
        Guid.NewGuid(),
        2
    );
    private readonly TripFlightLinkCodec codec = new();

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"links\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"links\":[],\"owner\":null}")]
    [InlineData("{\"schemaVersion\":2,\"links\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"links\":[{\"orderId\":null,\"linkedAt\":null}]}")]
    [InlineData("{\"schemaVersion\":1,\"links\":[{\"orderId\":1,\"orderId\":1,\"linkedAt\":1}]}")]
    public void Rejects_unknown_missing_invalid_and_duplicate_fields(string payload)
    {
        codec.Deserialize(payload, binding).FirstError.ShouldBe(TripErrors.Unavailable);
    }

    [Fact]
    public async Task Canonical_collection_only_contains_ids_and_linking_times_and_cannot_use_another_purpose()
    {
        using var ring = new TripsTestRing();
        var protector = new TripFlightLinkProtector(ring.Provider);
        var link = new TripFlightLink(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var collection = TripFlightLinks.Restore(binding.TripId, binding.Revision, [link]).Value;
        var payload = codec.Serialize(collection).Value;
        codec.Deserialize(payload, binding).Value.Links.Single().ShouldBe(link);
        payload.ShouldNotContain(binding.TripId.ToString());
        payload.ShouldNotContain(binding.Owner.Issuer);
        var cipher = protector.Protect(payload, binding).Value;
        cipher.ShouldNotContain(link.OrderId.ToString());
        protector.Unprotect(cipher, binding).Value.ShouldBe(payload);
        foreach (
            var otherBinding in new[]
            {
                binding with
                {
                    Revision = 3,
                },
                binding with
                {
                    TripId = Guid.NewGuid(),
                },
                binding with
                {
                    Owner = OwnerIdentity
                        .Create("https://other.fictional.test", binding.Owner.Subject)
                        .Value,
                },
                binding with
                {
                    Owner = OwnerIdentity.Create(binding.Owner.Issuer, Guid.NewGuid()).Value,
                },
            }
        )
            protector.Unprotect(cipher, otherBinding).FirstError.ShouldBe(TripErrors.Unavailable);
        var corrupted = Convert.FromBase64String(cipher);
        corrupted[^1] ^= 1;
        protector
            .Unprotect(Convert.ToBase64String(corrupted), binding)
            .FirstError.ShouldBe(TripErrors.Unavailable);
        var planBinding = new TripPlanBinding(binding.Owner, binding.TripId, binding.Revision);
        (
            await ring.Protector.UnprotectAsync(
                cipher,
                planBinding,
                TestContext.Current.CancellationToken
            )
        ).IsError.ShouldBeTrue();
        var planEnvelope = (
            await ring.Protector.ProtectAsync(
                payload,
                planBinding,
                TestContext.Current.CancellationToken
            )
        ).Value;
        protector.Unprotect(planEnvelope, binding).FirstError.ShouldBe(TripErrors.Unavailable);
    }

    [Fact]
    public void Fails_closed_for_oversize_input_and_absent_keys()
    {
        using var missing = new TripsProtectionProvider(
            Options.Create(new TripsProtectionOptions()),
            TimeProvider.System
        );
        var protector = new TripFlightLinkProtector(missing);
        protector.IsReady.ShouldBeFalse();
        protector.Protect("{}", binding).FirstError.ShouldBe(TripErrors.Unavailable);
        var oversized = new string('x', ITripFlightLinkProtector.MaximumBytes + 1);
        protector.Unprotect(oversized, binding).FirstError.ShouldBe(TripErrors.Unavailable);
        codec.Deserialize(oversized, binding).FirstError.ShouldBe(TripErrors.Unavailable);
    }
}
