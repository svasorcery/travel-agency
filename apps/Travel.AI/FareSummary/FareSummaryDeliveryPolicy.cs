using JasperFx;
using JasperFx.CodeGeneration;
using Travel.IntegrationContracts.AI.FareSummary;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.AI.FareSummary;

public sealed class FareSummaryDeliveryPolicy : IHandlerPolicy
{
    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(FareSummaryRequested)))
            chain.OnException<Exception>().Discard();
    }
}
