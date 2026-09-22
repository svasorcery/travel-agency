using Travel.Modules.Flights.Application.ReadModels;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class ReconcileOrderReadModelHandler
{
    [WolverineHandler]
    public static async Task Handle(
        ReconcileOrderReadModel message,
        IOrderReadModelReconciler reconciler,
        CancellationToken ct
    )
    {
        await reconciler.ReconcileAsync(
            message.AggregateId,
            OrderReadModelReconcileMode.Incremental,
            ct
        );
    }
}
