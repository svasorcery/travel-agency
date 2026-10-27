using ErrorOr;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public interface ITripBriefExtractionClient
{
    Task<ErrorOr<TripsBriefParsed>> ParseAsync(
        TripBriefExtractionInput input,
        CancellationToken ct
    );
}
