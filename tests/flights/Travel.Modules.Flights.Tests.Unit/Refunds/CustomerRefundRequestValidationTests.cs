using System.Text;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundRequestValidationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"refundId\":\"bad\"}")]
    [InlineData("{\"unknown\":\"sensitive\"}")]
    [InlineData(
        "{\"refundId\":\"00000000-0000-0000-0000-000000000123\",\"refundId\":\"00000000-0000-0000-0000-000000000123\"}"
    )]
    [InlineData("[]")]
    public void Malformed_or_duplicate_private_request_is_rejected_before_binding(string json) =>
        CustomerRefundRequestValidation.Valid(Encoding.UTF8.GetBytes(json)).ShouldBeFalse();

    [Fact]
    public void Bounded_typed_inconclusive_review_has_no_money_payload()
    {
        var json =
            "{\"refundId\":\"00000000-0000-0000-0000-000000000123\",\"expectedRevision\":1,"
            + "\"resolutionId\":\"00000000-0000-0000-0000-000000000124\",\"decision\":\"Inconclusive\","
            + "\"evidenceReference\":\"EVIDENCE-FICTIONAL-REVIEW\"}";
        CustomerRefundRequestValidation.Valid(Encoding.UTF8.GetBytes(json)).ShouldBeTrue();
    }
}
