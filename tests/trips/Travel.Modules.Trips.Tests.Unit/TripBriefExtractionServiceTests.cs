using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Modules.Trips.Tests.Unit;

public class TripBriefExtractionServiceTests
{
    [Fact]
    public async Task Exactly_one_attempt_and_matching_anchor_are_used()
    {
        var input = TripBriefCandidateValidatorTests.Input("Москва") with
        {
            Deadline = DateTimeOffset.UtcNow.AddSeconds(5),
        };
        var client = new FakeClient(
            (request, _) =>
                Task.FromResult<ErrorOr<TripsBriefParsed>>(
                    TripBriefCandidateValidatorTests.Reply(
                        request,
                        TripBriefCandidateValidatorTests.Extracted(request.Text, "Москва", "moscow")
                    )
                )
        );
        var result = await new TripBriefExtractionService(client, TimeProvider.System).ExtractAsync(
            input,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.ReferenceInstant.ShouldBe(input.ReferenceInstant);
        client.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("InvalidOutput", "Trips.InvalidModelOutput")]
    [InlineData("Busy", "Trips.BriefBusy")]
    [InlineData("BudgetExceeded", "Trips.BriefBusy")]
    [InlineData("DeadlineExceeded", "Trips.BriefDeadline")]
    [InlineData("ProcessingPolicyChanged", "Trips.ProcessingPolicyChanged")]
    [InlineData("AccountingUnavailable", "Trips.BriefUnavailable")]
    [InlineData("Refused", "Trips.BriefRefused")]
    public async Task Typed_failure_never_retries(string status, string error)
    {
        var input = TripBriefCandidateValidatorTests.Input("Москва") with
        {
            Deadline = DateTimeOffset.UtcNow.AddSeconds(5),
        };
        var client = new FakeClient(
            (request, _) =>
                Task.FromResult<ErrorOr<TripsBriefParsed>>(
                    new TripsBriefParsed(request.RequestId, request.ProcessingPolicyVersion, status)
                )
        );
        var result = await new TripBriefExtractionService(client, TimeProvider.System).ExtractAsync(
            input,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe(error);
        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Expired_request_never_calls_transport()
    {
        var input = TripBriefCandidateValidatorTests.Input("Москва") with
        {
            Deadline = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        var client = new FakeClient((_, _) => throw new Exception("private marker"));
        (
            await new TripBriefExtractionService(client, TimeProvider.System).ExtractAsync(
                input,
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Trips.BriefDeadline");
        client.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Transport_exception_is_sanitized_with_no_retry()
    {
        var input = TripBriefCandidateValidatorTests.Input("Москва") with
        {
            Deadline = DateTimeOffset.UtcNow.AddSeconds(5),
        };
        var client = new FakeClient(
            (_, _) => throw new InvalidOperationException("private marker")
        );
        var result = await new TripBriefExtractionService(client, TimeProvider.System).ExtractAsync(
            input,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Trips.BriefUnavailable");
        result.FirstError.Description.ShouldNotContain("private marker");
        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_ignoring_dependency_cannot_deliver_late_candidate_or_retry()
    {
        var pending = new TaskCompletionSource<ErrorOr<TripsBriefParsed>>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var input = TripBriefCandidateValidatorTests.Input("Москва") with
        {
            Deadline = DateTimeOffset.UtcNow.AddMilliseconds(50),
        };
        var client = new FakeClient((_, _) => pending.Task);
        var result = await new TripBriefExtractionService(client, TimeProvider.System).ExtractAsync(
            input,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Trips.BriefDeadline");
        client.Calls.ShouldBe(1);
        pending.SetException(new InvalidOperationException("private late marker"));
        await Task.Yield();
        client.Calls.ShouldBe(1);
    }

    private sealed class FakeClient(
        Func<TripBriefExtractionInput, CancellationToken, Task<ErrorOr<TripsBriefParsed>>> handler
    ) : ITripBriefExtractionClient
    {
        public int Calls { get; private set; }

        public Task<ErrorOr<TripsBriefParsed>> ParseAsync(
            TripBriefExtractionInput input,
            CancellationToken ct
        )
        {
            Calls++;
            return handler(input, ct);
        }
    }
}
