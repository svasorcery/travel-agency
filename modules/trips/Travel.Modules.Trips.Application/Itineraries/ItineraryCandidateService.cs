using System.Text;
using ErrorOr;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Application.Itineraries;

/// <summary>A read-only preview over an exact owner snapshot. Adoption remains an ordinary M1 CAS write.</summary>
public sealed class ItineraryCandidateService(
    TripPlanService plans,
    ITripPlanCodec planCodec,
    IItineraryFactSource factSource,
    IItineraryCandidateResponseEncoder encoder,
    ItineraryAdmissionGate admission,
    TimeProvider timeProvider
)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ErrorOr<PreparedCandidateResponse>> GenerateAsync(
        OwnerIdentity? owner,
        Guid tripId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (expectedRevision <= 0)
            return TripErrors.InvalidInput;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var loaded = await plans.GetAsync(owner, tripId, cancellationToken);
            if (loaded.IsError)
                return loaded.Errors;
            var plan = loaded.Value;
            if (plan.Revision != expectedRevision)
                return TripErrors.RevisionMismatch;
            if (plan.Days.Any(day => day.Blocks.Count != 0))
                return Error.Conflict(
                    "Trips.ItineraryRequiresEmptyPlan",
                    "Itinerary generation requires an empty saved plan."
                );

            // The same captured instant is used by scope, freshness, policy and independent validation.
            var asOfUtc = timeProvider.GetUtcNow();
            var scopeReason = ScopeReason(plan, asOfUtc);
            if (scopeReason is not null)
                return await PrepareAsync(
                    new ScopeUnsupportedCandidateResult(
                        expectedRevision,
                        plan.Destination,
                        scopeReason,
                        ItineraryPolicy.PolicyVersion,
                        asOfUtc
                    ),
                    plan,
                    cancellationToken
                );

            // Unready/bad facts must not consume admission or become an invented empty result.
            var source = await factSource.ReadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (source.IsError || !SourceMatches(plan, source.Value, asOfUtc))
                return Unavailable();
            if (!admission.TryEnter())
                return Error.Failure("Trips.ItineraryBusy", "Itinerary generation is busy.");
            try
            {
                using var deadline = new CancellationTokenSource(
                    TimeSpan.FromSeconds(2),
                    timeProvider
                );
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    deadline.Token
                );
                try
                {
                    var outcome = ItineraryPolicy.Generate(
                        plan.Brief,
                        plan.Destination,
                        source.Value,
                        asOfUtc,
                        linked.Token
                    );
                    linked.Token.ThrowIfCancellationRequested();
                    if (
                        outcome.IsError
                        || outcome.Value.Kind
                            is not (ItineraryOutcomeKind.Candidate or ItineraryOutcomeKind.Empty)
                    )
                        return Unavailable();
                    var validated = ItineraryCandidateValidator.Validate(
                        plan.Brief,
                        plan.Destination,
                        source.Value,
                        outcome.Value,
                        asOfUtc
                    );
                    linked.Token.ThrowIfCancellationRequested();
                    if (validated.IsError)
                        return Unavailable();
                    var response = await PrepareAsync(
                        new ComputedCandidateResult(
                            expectedRevision,
                            validated.Value,
                            source.Value
                        ),
                        plan,
                        linked.Token
                    );
                    linked.Token.ThrowIfCancellationRequested();
                    return response;
                }
                catch (Exception)
                    when (!cancellationToken.IsCancellationRequested
                        && deadline.IsCancellationRequested
                    )
                {
                    return Error.Failure(
                        "Trips.ItineraryDeadline",
                        "Itinerary generation exceeded its deadline."
                    );
                }
            }
            finally
            {
                // Await real cooperative work. Never release while an uncooperative encoder still runs.
                admission.Exit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Internal/source/codec details and private plan text must never escape or be logged here.
            return Unavailable();
        }
    }

    private async Task<ErrorOr<PreparedCandidateResponse>> PrepareAsync(
        ItineraryCandidateResult result,
        TripPlan original,
        CancellationToken cancellationToken
    )
    {
        var encoded = await encoder.EncodeAsync(result, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (
            encoded.IsError
            || encoded.Value.IsEmpty
            || encoded.Value.Length > TripPlanService.MaximumBodyBytes
        )
            return Unavailable();
        _ = StrictUtf8.GetCharCount(encoded.Value.Span);
        if (result is ComputedCandidateResult computed)
        {
            var days = computed
                .Outcome.Days.Select(day => new TripDayInput(
                    day.Date,
                    day.Blocks.Select(block => new TripBlockInput(
                            block.ProposedBlockId,
                            block.Title,
                            block.Note
                        ))
                        .ToArray()
                ))
                .ToArray();
            var projected = TripPlan.Restore(
                original.TripId,
                original.Owner,
                original.Revision,
                original.Title,
                original.Brief.ToInput(),
                original.Destination,
                days,
                original.CreatedAt,
                original.UpdatedAt
            );
            if (projected.IsError)
                return Unavailable();
            var size = planCodec.MeasureDetailBytes(
                projected.Value,
                reserveMaximumServerFields: true
            );
            if (size.IsError || size.Value < 0 || size.Value > TripPlanService.MaximumBodyBytes)
                return Unavailable();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var frozen = new PreparedCandidateResponse(encoded.Value);
        var current = await plans.GetAsync(original.Owner, original.TripId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (current.IsError)
            return current.Errors;
        if (current.Value.Revision != result.BaseRevision)
            return TripErrors.RevisionMismatch;
        if (
            result is ComputedCandidateResult final
            && timeProvider.GetUtcNow() >= final.Outcome.ExpiresAt
        )
            return Unavailable();
        return frozen;
    }

    private static string? ScopeReason(TripPlan plan, DateTimeOffset asOfUtc)
    {
        if (plan.Destination.DestinationId != "moscow")
            return "destinationUnsupported";
        var brief = plan.Brief;
        if (brief.EndDate.DayNumber - brief.StartDate.DayNumber + 1 is < 1 or > 3)
            return "dateRangeUnsupported";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(plan.Destination.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOfUtc, zone).DateTime);
        if (brief.StartDate < today)
            return "historicalDatesUnsupported";
        return brief.EndDate.DayNumber > today.DayNumber + 30 ? "horizonUnsupported" : null;
    }

    private static bool SourceMatches(
        TripPlan plan,
        ValidatedItineraryFacts? facts,
        DateTimeOffset asOfUtc
    ) =>
        facts is not null
        && facts.Destination.DestinationId == plan.Destination.DestinationId
        && facts.Destination.TimeZone == plan.Destination.TimeZone
        && facts.Destination.CatalogueVersion == plan.Destination.CatalogueVersion
        && asOfUtc >= facts.BuildAsOfUtc
        && asOfUtc < facts.ReleaseValidUntil
        && asOfUtc < facts.ModelSource.ValidUntil;

    private static Error Unavailable() =>
        Error.Failure("Trips.ItineraryUnavailable", "Itinerary generation is unavailable.");
}
