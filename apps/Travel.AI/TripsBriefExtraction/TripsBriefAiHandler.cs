using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Wolverine.Attributes;

namespace Travel.AI.TripsBriefExtraction;

public static class TripsBriefAiHandler
{
    [WolverineHandler]
    public static Task<TripsBriefParsed> Handle(
        TripsBriefRequested request,
        TripsBriefExtractor extractor,
        ITripsBriefLedger ledger,
        CancellationToken cancellationToken
    ) => extractor.ExtractAsync(request, ledger, cancellationToken);
}
