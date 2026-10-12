using System.Reflection;
using System.Text.Json;
using Shouldly;
using Travel.AI.FareSummary;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Xunit;

namespace Travel.Tests.Contract.Flights;

[Trait("Category", "Contract")]
public sealed class FareSummaryContractShapeTests
{
    [Theory]
    [InlineData(typeof(FareSummaryRequested), "travel.ai.fare-summary.requested")]
    [InlineData(typeof(FareSummaryPlanned), "travel.ai.fare-summary.planned")]
    public void Message_identity_is_v1_and_canonical_shared_type(Type type, string alias)
    {
        type.Assembly.GetName().Name.ShouldBe("Travel.IntegrationContracts.AI");
        type.Namespace.ShouldBe("Travel.IntegrationContracts.AI.FareSummary");
        var identity = type.GetCustomAttributesData()
            .Single(x =>
                x.AttributeType.FullName == "Wolverine.Attributes.MessageIdentityAttribute"
            );
        identity.ConstructorArguments.Single().Value.ShouldBe(alias);
        identity.NamedArguments.Single(x => x.MemberName == "Version").TypedValue.Value.ShouldBe(1);
        var types = new[]
        {
            typeof(FareSummaryAiHandler).Assembly,
            typeof(FareSummaryOrganizer).Assembly,
            type.Assembly,
        }
            .SelectMany(x => x.GetTypes())
            .Where(x => x.Name == type.Name)
            .ToArray();
        types.Length.ShouldBe(1);
    }

    [Fact]
    public void Exact_leaf_json_preserves_money_scope_identity_and_has_no_labels_or_profile_fields()
    {
        var id = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var deadline = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        var request = new FareSummaryRequested(
            id,
            new string('a', 64),
            "fare-summary-v1",
            "ru",
            deadline,
            [
                new(
                    "f0",
                    "change",
                    "change",
                    "Allowed",
                    "0.0000000000000000000000000001",
                    "GBP",
                    null,
                    [new("leg", 0)]
                ),
            ]
        );
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(request, options);
        using var document = JsonDocument.Parse(json);
        document
            .RootElement.EnumerateObject()
            .Select(x => x.Name)
            .ShouldBe(
                new[]
                {
                    "correlationId",
                    "fingerprint",
                    "promptVersion",
                    "locale",
                    "deadline",
                    "facts",
                }
            );
        var fact = document.RootElement.GetProperty("facts")[0];
        fact.GetProperty("amount").GetString().ShouldBe("0.0000000000000000000000000001");
        fact.EnumerateObject()
            .Select(x => x.Name)
            .ShouldBe(
                new[]
                {
                    "id",
                    "section",
                    "kind",
                    "state",
                    "amount",
                    "currency",
                    "quantity",
                    "scopes",
                }
            );
        var restored = JsonSerializer.Deserialize<FareSummaryRequested>(json, options)!;
        restored.CorrelationId.ShouldBe(id);
        restored.Fingerprint.ShouldBe(request.Fingerprint);
        restored.Facts[0].Scopes[0].Leg.ShouldBe(0);
        foreach (
            var type in new[]
            {
                typeof(FareSummaryRequested),
                typeof(FareSummaryFact),
                typeof(FareSummaryScope),
            }
        )
            type.GetProperties()
                .Select(x => x.Name)
                .ShouldNotContain(x =>
                    new[]
                    {
                        "Passenger",
                        "PassengerProfile",
                        "ProviderOfferRef",
                        "FareBrandName",
                        "FareBasisCode",
                        "RawText",
                    }.Contains(x)
                );
    }

    [Fact]
    public void Route_constants_are_isolated_from_nl_search_and_ready_roundtrips()
    {
        FareSummaryMessageIdentity.Subject.ShouldBe("travel.ai.fare_summary");
        FareSummaryMessageIdentity.QueueGroup.ShouldBe("travel.ai.fare_summary.workers");
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            DateTimeOffset.UtcNow,
            [new("f0", "change", "change", "Allowed", null, null, null, [new("offer")])]
        );
        var result = new FareSummaryPlanned(
            request.CorrelationId,
            request.Fingerprint,
            request.PromptVersion,
            request.Locale,
            "Ready",
            [new("change", [new(["f0"])])],
            10,
            5,
            0.000015m,
            "fictional"
        );
        var roundtrip = JsonSerializer.Deserialize<FareSummaryPlanned>(
            JsonSerializer.Serialize(result)
        )!;
        roundtrip.CorrelationId.ShouldBe(request.CorrelationId);
        roundtrip.Sections![0].Groups[0].FactIds.ShouldBe(new[] { "f0" });
        FareSummaryPlanValidator.Validate(request, roundtrip).IsError.ShouldBeFalse();
        FareSummaryPlanValidator
            .Validate(request, roundtrip with { Fingerprint = new string('b', 64) })
            .IsError.ShouldBeTrue();
    }
}
