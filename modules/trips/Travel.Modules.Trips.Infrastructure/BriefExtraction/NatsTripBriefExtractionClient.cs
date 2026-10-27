using ErrorOr;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Wolverine;

namespace Travel.Modules.Trips.Infrastructure.BriefExtraction;

public sealed class NatsTripBriefExtractionClient(IMessageBus bus, TimeProvider clock)
    : ITripBriefExtractionClient
{
    public async Task<ErrorOr<TripsBriefParsed>> ParseAsync(
        TripBriefExtractionInput input,
        CancellationToken ct
    )
    {
        var remaining = input.Deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return TripBriefErrors.Deadline;
        var request = new TripsBriefRequested(
            input.RequestId,
            input.ProcessingPolicyVersion,
            input.Text,
            input.Locale,
            input.ReferenceInstant,
            input.ReferenceZone,
            input.ReferenceLocalDate,
            input.Deadline
        );
        try
        {
            return await bus.InvokeAsync<TripsBriefParsed>(request, ct, timeout: remaining);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return TripBriefErrors.Deadline;
        }
        catch (Exception)
        {
            return TripBriefErrors.Unavailable;
        }
    }
}
