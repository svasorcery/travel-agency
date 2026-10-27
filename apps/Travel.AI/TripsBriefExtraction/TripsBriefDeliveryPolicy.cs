using JasperFx;
using JasperFx.CodeGeneration;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.AI.TripsBriefExtraction;

public sealed class TripsBriefDeliveryPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(TripsBriefRequested)))
            chain.OnException<Exception>().Discard();
    }
}
