using System.Text;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationRequestValidationTests
{
    [Theory]
    [InlineData("targetKind", "Cancellation,Confirmation")]
    [InlineData("decision", "RecordInconclusive,ConfirmNoEffect")]
    [InlineData("category", "Inconclusive,SupplierSupportAttestation")]
    [InlineData("destination", "Balance,Card")]
    [InlineData("composition", "CashOnly,Unpaid")]
    public void Manual_enums_require_one_exact_name(string field, string value)
    {
        var body =
            "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"targetKind\":\"Cancellation\",\"targetId\":\"00000000-0000-0000-0000-000000000002\",\"expectedRevision\":1,\"resolutionId\":\"00000000-0000-0000-0000-000000000003\",\"decision\":\"RecordInconclusive\",\"evidence\":{\"evidenceRef\":\"EVIDENCE-001\",\"category\":\"Inconclusive\",\"observedAt\":\"2026-10-05T12:00:00Z\",\"destination\":\"Balance\",\"settlement\":{\"composition\":\"CashOnly\",\"creditsKnownEmpty\":true,\"originalCashOnlyVerified\":false,\"unpaidOrderVerified\":false}}}";
        var old = field switch
        {
            "targetKind" => "Cancellation",
            "decision" => "RecordInconclusive",
            "category" => "Inconclusive",
            "destination" => "Balance",
            _ => "CashOnly",
        };
        body = body.Replace(
            "\"" + field + "\":\"" + old + "\"",
            "\"" + field + "\":\"" + value + "\""
        );
        CancellationRequestValidation
            .Valid(Encoding.UTF8.GetBytes(body), "review/resolve")
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(
        "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedBookingVersion\":1,\"actor\":\"invented\"}"
    )]
    [InlineData(
        "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedBookingVersion\":1,\"ExpectedBookingVersion\":1}"
    )]
    [InlineData(
        "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedBookingVersion\":0}"
    )]
    [InlineData(
        "{\"aggregateId\":\"00000000-0000-0000-0000-000000000000\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedBookingVersion\":1}"
    )]
    public void Prepare_rejects_missing_unknown_duplicate_empty_or_invalid_fields(string json) =>
        CancellationRequestValidation
            .Valid(Encoding.UTF8.GetBytes(json), "prepare")
            .ShouldBeFalse();

    [Fact]
    public void Prepare_accepts_exact_body() =>
        CancellationRequestValidation
            .Valid(
                Encoding.UTF8.GetBytes(
                    "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedBookingVersion\":1}"
                ),
                "prepare"
            )
            .ShouldBeTrue();

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("\"true\"")]
    public void Consent_requires_explicit_boolean_true(string accepted) =>
        CancellationRequestValidation
            .Valid(
                Encoding.UTF8.GetBytes(
                    "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"operationId\":\"00000000-0000-0000-0000-000000000002\",\"expectedOperationRevision\":2,\"termsRevision\":1,\"termsHash\":\""
                        + new string('a', 64)
                        + "\",\"noticeVersion\":\"cancellation-v1\",\"accepted\":"
                        + accepted
                        + "}"
                ),
                "consent"
            )
            .ShouldBeFalse();

    [Theory]
    [InlineData("\"invented\"")]
    [InlineData("1")]
    public void Resolve_rejects_unknown_or_numeric_enum(string target) =>
        CancellationRequestValidation
            .Valid(
                Encoding.UTF8.GetBytes(
                    "{\"aggregateId\":\"00000000-0000-0000-0000-000000000001\",\"targetKind\":"
                        + target
                        + ",\"targetId\":\"00000000-0000-0000-0000-000000000002\",\"expectedRevision\":1,\"resolutionId\":\"00000000-0000-0000-0000-000000000003\",\"decision\":\"RecordInconclusive\",\"evidence\":{\"evidenceRef\":\"EVIDENCE-001\",\"category\":\"Inconclusive\",\"observedAt\":\"2026-10-05T12:00:00Z\"}}"
                ),
                "review/resolve"
            )
            .ShouldBeFalse();
}
