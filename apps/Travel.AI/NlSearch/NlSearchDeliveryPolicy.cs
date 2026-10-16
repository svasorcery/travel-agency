using JasperFx;
using JasperFx.CodeGeneration;
using Travel.IntegrationContracts.AI.NlSearch;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.AI.NlSearch;

public sealed class NlSearchDeliveryPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(NlSearchRequested)))
            chain.OnException<Exception>().Discard();
    }
}
