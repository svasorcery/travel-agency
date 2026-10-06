using Travel.Modules.Flights.Application.Cancellation;

namespace Travel.Modules.Flights.Infrastructure.Cancellation;

public sealed class ProcessDispatchInstanceIdentity : IDispatchInstanceIdentity
{
    public Guid Id { get; } = Guid.NewGuid();
}
