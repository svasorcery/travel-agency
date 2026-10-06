using System.Globalization;
using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationFingerprintTests
{
    [Fact]
    public void Equivalent_decimal_and_instant_have_the_same_culture_independent_consent_hash()
    {
        var input = CancellationTestData.Input() with
        {
            Refund = CancellationTestData.Money(12.50m),
        };
        var expected = CancellationTerms.Create(input, CancellationTestData.Now).Value.Hash;
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var equivalent = input with
            {
                Refund = CancellationTestData.Money(12.5000m),
                ExpiresAt = input.ExpiresAt!.Value.ToOffset(TimeSpan.FromHours(3)),
            };
            CancellationTerms
                .Create(equivalent, CancellationTestData.Now)
                .Value.Hash.ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [MemberData(nameof(ChangedInputs), DisableDiscoveryEnumeration = true)]
    public void Meaningful_changes_cannot_reuse_the_original_consent_hash(
        CancellationTermsInput changed
    )
    {
        var original = CancellationTerms
            .Create(CancellationTestData.Input(), CancellationTestData.Now)
            .Value;
        var replacement = CancellationTerms.Create(changed, CancellationTestData.Now);
        replacement.IsError.ShouldBeFalse();
        replacement.Value.Hash.ShouldNotBe(original.Hash);
    }

    public static IEnumerable<object[]> ChangedInputs()
    {
        var input = CancellationTestData.Input();
        foreach (
            var changed in new[]
            {
                input with
                {
                    Revision = 2,
                },
                input with
                {
                    AggregateId = Guid.Parse("00000000-0000-0000-0000-000000000104"),
                },
                input with
                {
                    OwnerId = Guid.Parse("00000000-0000-0000-0000-000000000105"),
                },
                input with
                {
                    OperationId = Guid.Parse("00000000-0000-0000-0000-000000000106"),
                },
                input with
                {
                    ProviderOrderRef = "ord_fictional_2",
                },
                input with
                {
                    ProviderCancellationRef = "occ_fictional_2",
                },
                input with
                {
                    ItineraryPartyHash = new string('b', 64),
                },
                input with
                {
                    Refund = CancellationTestData.Money(18m),
                },
                input with
                {
                    Refund = CancellationTestData.Money(17.25m, "EUR"),
                },
                input with
                {
                    Destination = CancellationRefundDestination.Card,
                },
                input with
                {
                    ExpiresAt = input.ExpiresAt!.Value.AddSeconds(1),
                },
                input with
                {
                    NoticeVersion = "cancellation-v2",
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Provenance = CancellationResolutionSource.OperatorVerified,
                    },
                },
            }
        )
            yield return new object[] { changed };
    }

    [Fact]
    public void Opaque_binding_field_separators_cannot_collide()
    {
        var input = CancellationTestData.Input();
        var a = CancellationTerms
            .Create(
                input with
                {
                    ProviderOrderRef = "order|other",
                    ProviderCancellationRef = "cancellation",
                },
                CancellationTestData.Now
            )
            .Value;
        var b = CancellationTerms
            .Create(
                input with
                {
                    ProviderOrderRef = "order",
                    ProviderCancellationRef = "other|cancellation",
                },
                CancellationTestData.Now
            )
            .Value;
        a.Hash.ShouldNotBe(b.Hash);
    }
}
