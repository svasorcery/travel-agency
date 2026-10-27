using System.Text.Json;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Xunit;

namespace Travel.Tests.Contract.Trips;

[Trait("Category", "Contract")]
public sealed class TripsBriefExtractionContractShapeTests
{
    [Theory]
    [InlineData(typeof(TripsBriefRequested), "travel.ai.trips-brief.requested")]
    [InlineData(typeof(TripsBriefParsed), "travel.ai.trips-brief.parsed")]
    public void Shared_messages_have_distinct_versioned_identity(Type type, string alias)
    {
        var identity = type.GetCustomAttributesData()
            .Single(x =>
                x.AttributeType.FullName == "Wolverine.Attributes.MessageIdentityAttribute"
            );
        identity.ConstructorArguments.Single().Value.ShouldBe(alias);
        identity.NamedArguments.Single(x => x.MemberName == "Version").TypedValue.Value.ShouldBe(1);
        type.Assembly.GetName().Name.ShouldBe("Travel.IntegrationContracts.AI");
    }

    [Fact]
    public void Request_transmits_only_the_admitted_minimum_and_never_prints_text()
    {
        var now = new DateTimeOffset(2026, 10, 27, 21, 30, 0, TimeSpan.Zero);
        var input = new TripsBriefRequested(
            Guid.NewGuid(),
            "policy",
            "FICTIONAL_PRIVATE_MARKER",
            "ru",
            now,
            "Europe/Moscow",
            new DateOnly(2026, 10, 28),
            now.AddSeconds(5)
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        json.RootElement.EnumerateObject()
            .Select(x => x.Name)
            .ShouldBe(
                new[]
                {
                    "correlationId",
                    "processingPolicyVersion",
                    "text",
                    "locale",
                    "referenceInstant",
                    "referenceZone",
                    "referenceLocalDate",
                    "deadline",
                }
            );
        input.ToString().ShouldNotContain("FICTIONAL_PRIVATE_MARKER");
    }

    [Fact]
    public void Public_policy_digest_binds_processor_model_and_notice_without_private_data()
    {
        var digest = TripsBriefPolicyIdentity.Compute("fictional", "model-a", "trips-brief-ru-v1");
        digest.Length.ShouldBe(64);
        TripsBriefPolicyIdentity
            .Compute("anthropic", "model<>&+'", "trips-brief-ru-v1")
            .ShouldBe(
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(
                            "[\"anthropic\",\"model<>&+'\",\"trips-brief-ru-v1\"]"
                        )
                    )
                )
            );
        digest.ShouldBe(
            TripsBriefPolicyIdentity.Compute("fictional", "model-a", "trips-brief-ru-v1")
        );
        digest.ShouldNotBe(
            TripsBriefPolicyIdentity.Compute("other", "model-a", "trips-brief-ru-v1")
        );
        digest.ShouldNotBe(
            TripsBriefPolicyIdentity.Compute("fictional", "model-b", "trips-brief-ru-v1")
        );
        digest.ShouldNotBe(TripsBriefPolicyIdentity.Compute("fictional", "model-a", "different"));
    }

    [Fact]
    public void Candidate_has_seven_slots_and_no_money_origin_or_action_fields()
    {
        var missing = new TripsBriefField("missing", null, [], []);
        var fields = new TripsBriefFields(
            missing,
            missing,
            missing,
            missing,
            missing,
            missing,
            missing
        );
        using var json = JsonDocument.Parse(
            JsonSerializer.Serialize(fields, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        );
        json.RootElement.EnumerateObject()
            .Select(x => x.Name)
            .ShouldBe(
                new[]
                {
                    "destinationId",
                    "startDate",
                    "endDate",
                    "durationDays",
                    "adults",
                    "interests",
                    "pace",
                }
            );
        typeof(TripsBriefParsed).GetProperties().Select(x => x.Name).ShouldNotContain("OwnerId");
        fields.ToString().ShouldBe(nameof(TripsBriefFields));
    }
}
